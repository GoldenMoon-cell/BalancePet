using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Reads the aggregate usage exposed by OpenAI-compatible /v1/usage relays.
/// Only the date, amount and currency are returned; credentials and response
/// payloads never leave the main process.
/// </summary>
public sealed class ServerUsageSummaryProvider(HttpClient http)
{
    private const long MaxResponseBytes = 2L * 1024 * 1024;

    public async Task<ServerUsageSummary?> FetchAsync(
        MonitorProfile profile,
        string token,
        CancellationToken cancellationToken = default)
    {
        var preset = BalancePresetCatalog.NormalizeId(profile.PresetId);
        if (preset is not (BalancePresetCatalog.Auto or BalancePresetCatalog.V1Usage)
            || string.IsNullOrWhiteSpace(token)) return null;

        var site = BalancePresetCatalog.ResolveSiteUrl(profile);
        if (string.IsNullOrWhiteSpace(site)) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{site.TrimEnd('/')}/v1/usage");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(token));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength is > MaxResponseBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions
        {
            MaxDepth = 32,
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }, cancellationToken);

        var root = document.RootElement;
        var currency = ReadString(root, "unit", "data.unit", "currency", "data.currency");
        if (string.IsNullOrWhiteSpace(currency)) currency = profile.Currency;
        currency = CleanCurrency(currency);

        if (!TryReadProperty(root, "daily_usage", out var dailyUsage)
            && !TryReadProperty(root, "data.daily_usage", out dailyUsage)) return null;
        if (dailyUsage.ValueKind != JsonValueKind.Array) return null;

        var days = new List<ServerUsageDay>();
        foreach (var item in dailyUsage.EnumerateArray())
        {
            var date = ReadString(item, "date", "day").Trim();
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)) continue;
            if (!TryReadNumber(item, out var amount, "actual_cost", "cost")) continue;
            if (!double.IsFinite(amount) || amount < 0 || amount > 10_000_000_000) continue;
            days.Add(new ServerUsageDay(date, amount));
        }

        return new ServerUsageSummary(profile.Id, currency, days, DateTimeOffset.Now);
    }

    private static string StripBearer(string token)
        => token.Trim().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token.Trim()[7..].Trim()
            : token.Trim();

    private static string CleanCurrency(string? value)
    {
        var currency = (value ?? "").Trim();
        return currency.Length is > 0 and <= 12 && currency.All(character => !char.IsControl(character))
            ? currency.ToUpperInvariant()
            : "USD";
    }

    private static string ReadString(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
            if (TryReadProperty(root, path, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        return "";
    }

    private static bool TryReadNumber(JsonElement root, out double number, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!TryReadProperty(root, path, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number)) return true;
            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return true;
        }
        number = 0;
        return false;
    }

    private static bool TryReadProperty(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }
        return true;
    }
}

public sealed record ServerUsageSummary(
    string AccountId,
    string Currency,
    IReadOnlyList<ServerUsageDay> Days,
    DateTimeOffset UpdatedAt);

public sealed record ServerUsageDay(string Date, double Amount);
