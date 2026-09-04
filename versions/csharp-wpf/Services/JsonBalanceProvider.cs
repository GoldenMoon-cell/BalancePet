using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

public sealed class JsonBalanceProvider(HttpClient http)
{
    public Task<BalanceSnapshot> FetchWithRetryAsync(MonitorProfile profile, string token, CancellationToken cancellationToken = default)
    {
        return FetchWithRetryCoreAsync(() => FetchAsync(profile, token, cancellationToken), cancellationToken);
    }

    public Task<BalanceSnapshot> FetchWithRetryAsync(PetSettings settings, string token, CancellationToken cancellationToken = default)
    {
        return FetchWithRetryCoreAsync(() => FetchAsync(settings, token, cancellationToken), cancellationToken);
    }

    private static async Task<BalanceSnapshot> FetchWithRetryCoreAsync(Func<Task<BalanceSnapshot>> fetch, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await fetch();
            }
            catch (Exception error) when (IsTransient(error, cancellationToken))
            {
                last = error;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 1 : 3), cancellationToken);
            }
        }

        throw new HttpRequestException("请求失败，已自动重试 2 次。", last);
    }

    public Task<BalanceSnapshot> FetchAsync(MonitorProfile profile, string token, CancellationToken cancellationToken = default)
    {
        var presetId = BalancePresetCatalog.NormalizeId(profile.PresetId);
        if (presetId == BalancePresetCatalog.Auto)
            return FetchAutoAsync(profile, token, cancellationToken);
        if (presetId == BalancePresetCatalog.V1Usage)
            return FetchV1UsageAsync(BalancePresetCatalog.BuildEndpoint(BalancePresetCatalog.ResolveSiteUrl(profile), presetId), profile.Currency, token, cancellationToken);
        if (presetId == BalancePresetCatalog.NewApiToken)
            return FetchNewApiTokenAsync(BalancePresetCatalog.ResolveSiteUrl(profile), profile.Currency, token, cancellationToken);

        var settings = new PetSettings
        {
            Endpoint = profile.Endpoint,
            AuthMode = profile.AuthMode,
            HeaderName = profile.HeaderName,
            BalancePath = profile.BalancePath,
            Currency = profile.Currency,
            RefreshSeconds = profile.RefreshSeconds,
            LowThreshold = profile.LowThreshold
        };
        return FetchAsync(settings, token, cancellationToken);
    }

    public async Task<BalanceSnapshot> FetchAsync(PetSettings settings, string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (settings.AuthMode.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("x-api-key", token);
            else if (settings.AuthMode.Equals("custom", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation(settings.HeaderName, token);
            else if (settings.AuthMode.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("Authorization", NormalizeAuthorization(token));
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(token));
                if (settings.AuthMode.Equals("websee-session", StringComparison.OrdinalIgnoreCase))
                {
                    var endpoint = new Uri(settings.Endpoint);
                    request.Headers.Referrer = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/dashboard");
                    request.Headers.TryAddWithoutValidation("X-User-UI-Request", "1");
                    request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
                }
            }
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {detail[..Math.Min(detail.Length, 180)]}", null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var value = ReadPath(document.RootElement, settings.BalancePath);
        if (!value.HasValue || !TryParseAmount(value.Value, out var amount) || !double.IsFinite(amount))
            throw new InvalidDataException($"JSON path not found: {settings.BalancePath}");
        return new BalanceSnapshot(amount, settings.Currency, DateTimeOffset.Now, BalancePresetCatalog.Custom);
    }

    private async Task<BalanceSnapshot> FetchAutoAsync(MonitorProfile profile, string token, CancellationToken cancellationToken)
    {
        var siteUrl = BalancePresetCatalog.ResolveSiteUrl(profile);
        var failures = new List<string>();
        try
        {
            var endpoint = BalancePresetCatalog.BuildEndpoint(siteUrl, BalancePresetCatalog.V1Usage);
            return await FetchV1UsageAsync(endpoint, profile.Currency, token, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or InvalidDataException or JsonException)
        {
            failures.Add($"/v1/usage：{ShortError(error)}");
        }

        try
        {
            return await FetchNewApiTokenAsync(siteUrl, profile.Currency, token, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or InvalidDataException or JsonException)
        {
            failures.Add($"/api/usage/token：{ShortError(error)}");
        }

        throw new InvalidDataException($"未识别到支持的余额接口。{string.Join("；", failures)}");
    }

    private async Task<BalanceSnapshot> FetchV1UsageAsync(string endpoint, string fallbackCurrency, string token, CancellationToken cancellationToken)
    {
        using var document = await GetBearerJsonAsync(endpoint, token, cancellationToken);
        var root = document.RootElement;
        double amount;
        if (root.TryGetProperty("subscription", out _) && TryReadAmount(root, out amount, "remaining"))
        {
            // Subscription remaining is the effective minimum across its active windows.
        }
        else if (root.TryGetProperty("quota", out _) && TryReadAmount(root, out amount, "quota.remaining", "remaining"))
        {
        }
        else if (!TryReadAmount(root, out amount, "balance", "remaining", "data.balance", "data.remaining"))
        {
            throw new InvalidDataException("/v1/usage 响应中没有找到 balance 或 remaining。");
        }

        if (!double.IsFinite(amount)) throw new InvalidDataException("/v1/usage 返回的余额不是有效数字。");
        var currency = ReadString(root, "unit", "quota.unit", "data.unit");
        if (string.IsNullOrWhiteSpace(currency)) currency = string.IsNullOrWhiteSpace(fallbackCurrency) ? "USD" : fallbackCurrency;
        return new BalanceSnapshot(amount, currency.Trim().ToUpperInvariant(), DateTimeOffset.Now, BalancePresetCatalog.V1Usage);
    }

    private async Task<BalanceSnapshot> FetchNewApiTokenAsync(string siteUrl, string fallbackCurrency, string token, CancellationToken cancellationToken)
    {
        var endpoint = BalancePresetCatalog.BuildEndpoint(siteUrl, BalancePresetCatalog.NewApiToken);
        using var usageDocument = await GetBearerJsonAsync(endpoint, token, cancellationToken);
        var root = usageDocument.RootElement;
        if (ReadBoolean(root, "data.unlimited_quota"))
            throw new InvalidDataException("该 API Key 为无限额度，接口没有可显示的有限余额。");
        if (!TryReadAmount(root, out var rawQuota, "data.total_available"))
            throw new InvalidDataException("New API 响应中没有找到 data.total_available。");

        using var statusDocument = await GetJsonAsync($"{BalancePresetCatalog.NormalizeSiteUrl(siteUrl)}/api/status", cancellationToken);
        var status = statusDocument.RootElement;
        if (!TryReadAmount(status, out var quotaPerUnit, "data.quota_per_unit", "data.currency.quota_per_unit") || quotaPerUnit <= 0)
            throw new InvalidDataException("New API 未返回有效的 quota_per_unit，无法把原始额度换算为余额。");

        var amountUsd = rawQuota / quotaPerUnit;
        var displayType = ReadString(status, "data.quota_display_type", "data.currency.quota_display_type").Trim().ToUpperInvariant();
        var amount = amountUsd;
        var currency = string.IsNullOrWhiteSpace(fallbackCurrency) ? "USD" : fallbackCurrency.Trim().ToUpperInvariant();
        if (displayType == "TOKENS")
        {
            amount = rawQuota;
            currency = "TOKENS";
        }
        else if (displayType == "CNY")
        {
            if (!TryReadAmount(status, out var exchangeRate, "data.usd_exchange_rate", "data.currency.usd_exchange_rate") || exchangeRate <= 0)
                throw new InvalidDataException("New API 使用 CNY 展示，但未返回有效的 usd_exchange_rate。");
            amount = amountUsd * exchangeRate;
            currency = "CNY";
        }
        else if (displayType == "CUSTOM")
        {
            if (!TryReadAmount(status, out var exchangeRate, "data.custom_currency_exchange_rate", "data.currency.custom_currency_exchange_rate") || exchangeRate <= 0)
                throw new InvalidDataException("New API 使用自定义货币，但未返回有效的换算率。");
            amount = amountUsd * exchangeRate;
            var symbol = ReadString(status, "data.custom_currency_symbol", "data.currency.custom_currency_symbol").Trim();
            currency = string.IsNullOrWhiteSpace(symbol) ? currency : symbol;
        }
        else if (displayType == "USD")
        {
            currency = "USD";
        }

        if (!double.IsFinite(amount)) throw new InvalidDataException("New API 返回的余额不是有效数字。");
        return new BalanceSnapshot(amount, currency, DateTimeOffset.Now, BalancePresetCatalog.NewApiToken);
    }

    private async Task<JsonDocument> GetBearerJsonAsync(string endpoint, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(token));
        return await SendJsonAsync(request, cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        return await SendJsonAsync(request, cancellationToken);
    }

    private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {detail[..Math.Min(detail.Length, 180)]}", null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static bool IsTransient(Exception error, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        if (error is TaskCanceledException) return true;
        return error is HttpRequestException httpError &&
            (!httpError.StatusCode.HasValue || (int)httpError.StatusCode.Value is 408 or 425 or 429 or >= 500);
    }

    private static string StripBearer(string token) => token.Trim().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token.Trim()[7..].Trim() : token.Trim();

    private static string NormalizeAuthorization(string token)
    {
        var value = token.Trim();
        return value.Contains(' ', StringComparison.Ordinal) ? value : $"Bearer {value}";
    }

    private static bool TryReadAmount(JsonElement root, out double amount, params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = ReadPath(root, path);
            if (value.HasValue && TryParseAmount(value.Value, out amount)) return true;
        }
        amount = 0;
        return false;
    }

    private static string ReadString(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            var value = ReadPath(root, path);
            if (value.HasValue && value.Value.ValueKind == JsonValueKind.String)
                return value.Value.GetString() ?? "";
        }
        return "";
    }

    private static bool ReadBoolean(JsonElement root, string path)
    {
        var value = ReadPath(root, path);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.True;
    }

    private static string ShortError(Exception error)
    {
        var text = error.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 120 ? text : text[..120] + "…";
    }

    private static JsonElement? ReadPath(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(part, out current)) return null;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength()) return null;
                current = current[index];
            }
            else return null;
        }
        return current;
    }

    private static bool TryParseAmount(JsonElement value, out double amount)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString() ?? "",
            _ => ""
        };
        text = text.Trim().Replace(",", "", StringComparison.Ordinal)
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("¥", "", StringComparison.Ordinal);
        foreach (var symbol in new[] { "USD", "CNY", "RMB" }) text = text.Replace(symbol, "", StringComparison.OrdinalIgnoreCase);
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
    }
}

internal static class JsonElementExtensions
{
    public static string GetStringOrNumber(this JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String => value.GetString() ?? "",
        _ => ""
    };
}
