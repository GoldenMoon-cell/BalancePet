using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Reads the read-only per-token log exposed by New API compatible relays.
/// The API key is sent only to the configured relay host and is never included
/// in exceptions, logs, or persisted usage data.
/// </summary>
public sealed class NewApiUsageProvider(HttpClient http)
{
    private const int MaxLogItems = 5000;
    private const long MaxResponseBytes = 8L * 1024 * 1024;
    private static readonly TimeSpan PricingCacheLifetime = TimeSpan.FromMinutes(10);
    private readonly object _pricingGate = new();
    private readonly Dictionary<string, PricingCacheEntry> _pricing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _matchedGate = new();
    private readonly HashSet<string> _matchedLogKeys = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<NewApiUsageRecord>> FetchRecentAsync(
        MonitorProfile profile,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(profile) || string.IsNullOrWhiteSpace(token)) return Array.Empty<NewApiUsageRecord>();

        var site = BalancePresetCatalog.ResolveSiteUrl(profile);
        if (string.IsNullOrWhiteSpace(site)) return Array.Empty<NewApiUsageRecord>();
        var pricing = await GetPricingAsync(site, cancellationToken);
        if (pricing is null) return Array.Empty<NewApiUsageRecord>();

        var endpoint = $"{site.TrimEnd('/')}/api/log/token?key={Uri.EscapeDataString(StripBearer(token))}";
        using var document = await GetJsonAsync(endpoint, cancellationToken);
        var records = ParseRecords(document.RootElement, pricing);
        return records.Count > MaxLogItems ? records.Take(MaxLogItems).ToArray() : records;
    }

    public bool TryTakeMatch(
        IReadOnlyList<NewApiUsageRecord> records,
        UsageEventSnapshot target,
        TimeSpan tolerance,
        out NewApiUsageRecord? match)
    {
        match = records
            .Where(record => IsCandidate(record, target, tolerance))
            .OrderBy(record => Score(record, target))
            .FirstOrDefault();
        if (match is null) return false;

        lock (_matchedGate)
        {
            if (!_matchedLogKeys.Add(match.MatchKey))
            {
                match = records
                    .Where(record => IsCandidate(record, target, tolerance))
                    .Where(record => !_matchedLogKeys.Contains(record.MatchKey))
                    .OrderBy(record => Score(record, target))
                    .FirstOrDefault();
                if (match is null) return false;
                _matchedLogKeys.Add(match.MatchKey);
            }
        }
        return true;
    }

    public static bool Supports(MonitorProfile profile)
    {
        var preset = BalancePresetCatalog.NormalizeId(profile.PresetId);
        return preset is BalancePresetCatalog.Auto or BalancePresetCatalog.NewApiToken;
    }

    private async Task<QuotaPricing?> GetPricingAsync(string site, CancellationToken cancellationToken)
    {
        var key = site.TrimEnd('/');
        lock (_pricingGate)
        {
            if (_pricing.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.UpdatedAt < PricingCacheLifetime)
                return cached.Pricing;
        }

        using var document = await GetJsonAsync($"{key}/api/status", cancellationToken);
        var root = document.RootElement;
        if (!TryReadNumber(root, out var quotaPerUnit, "data.quota_per_unit", "data.currency.quota_per_unit") || quotaPerUnit <= 0)
            return null;

        var displayType = ReadString(root, "data.quota_display_type", "data.currency.quota_display_type").Trim().ToUpperInvariant();
        var currency = "USD";
        var multiplier = 1d / quotaPerUnit;
        if (displayType == "TOKENS")
        {
            currency = "TOKENS";
            multiplier = 1d;
        }
        else if (displayType == "CNY")
        {
            if (!TryReadNumber(root, out var exchangeRate, "data.usd_exchange_rate", "data.currency.usd_exchange_rate") || exchangeRate <= 0)
                return null;
            currency = "CNY";
            multiplier *= exchangeRate;
        }
        else if (displayType == "CUSTOM")
        {
            if (!TryReadNumber(root, out var exchangeRate, "data.custom_currency_exchange_rate", "data.currency.custom_currency_exchange_rate") || exchangeRate <= 0)
                return null;
            currency = ReadString(root, "data.custom_currency_symbol", "data.currency.custom_currency_symbol").Trim();
            if (currency.Length == 0) currency = "USD";
            multiplier *= exchangeRate;
        }

        var pricing = new QuotaPricing(multiplier, currency);
        lock (_pricingGate) _pricing[key] = new PricingCacheEntry(pricing, DateTimeOffset.UtcNow);
        return pricing;
    }

    private async Task<JsonDocument> GetJsonAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidDataException("中转站日志响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static IReadOnlyList<NewApiUsageRecord> ParseRecords(JsonElement root, QuotaPricing pricing)
    {
        if (root.ValueKind != JsonValueKind.Object || ReadBoolean(root, "success") == false) return Array.Empty<NewApiUsageRecord>();
        if (!root.TryGetProperty("data", out var data)) return Array.Empty<NewApiUsageRecord>();
        var values = data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray()
            : data.ValueKind == JsonValueKind.Object && data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray()
                : Enumerable.Empty<JsonElement>();

        var records = new List<NewApiUsageRecord>();
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.Object) continue;
            var type = ReadInt(value, "type");
            if (type.HasValue && type.Value != 2) continue;
            if (!TryReadNumber(value, out var quota, "quota") || quota < 0 || !double.IsFinite(quota)) continue;
            var createdAt = ReadTimestamp(value, "created_at", "createdAt", "timestamp");
            if (!createdAt.HasValue) continue;
            var model = ReadString(value, "model_name", "modelName", "model").Trim();
            records.Add(new NewApiUsageRecord(
                ReadString(value, "id", "request_id", "requestId").Trim(),
                createdAt.Value,
                model,
                ReadLong(value, "prompt_tokens", "input_tokens", "inputTokens"),
                ReadLong(value, "completion_tokens", "output_tokens", "outputTokens"),
                ReadLong(value, "cached_tokens", "cache_read_tokens", "cacheReadTokens"),
                quota,
                quota * pricing.Multiplier,
                pricing.Currency));
        }
        return records;
    }

    private static bool IsCandidate(NewApiUsageRecord record, UsageEventSnapshot target, TimeSpan tolerance)
    {
        if (Math.Abs((record.OccurredAt - target.OccurredAt).TotalSeconds) > tolerance.TotalSeconds) return false;
        if (!string.IsNullOrWhiteSpace(record.Model) && !string.IsNullOrWhiteSpace(target.Model)
            && !string.Equals(record.Model, target.Model, StringComparison.OrdinalIgnoreCase)) return false;

        var hasRemoteCounters = record.InputTokens.HasValue || record.OutputTokens.HasValue;
        var matchedCounter = false;
        if (target.OutputTokens.HasValue && record.OutputTokens.HasValue)
        {
            if (target.OutputTokens.Value != record.OutputTokens.Value) return false;
            matchedCounter = true;
        }
        if (target.InputTokens.HasValue && record.InputTokens.HasValue)
        {
            var inputMatches = target.InputTokens.Value == record.InputTokens.Value;
            if (!inputMatches && record.CacheReadTokens.HasValue)
                inputMatches = target.InputTokens.Value == record.InputTokens.Value + record.CacheReadTokens.Value;
            if (!inputMatches && target.CacheReadTokens.HasValue)
                inputMatches = target.InputTokens.Value - target.CacheReadTokens.Value == record.InputTokens.Value;
            if (!inputMatches && hasRemoteCounters) return false;
            matchedCounter |= inputMatches;
        }
        return !hasRemoteCounters || matchedCounter;
    }

    private static double Score(NewApiUsageRecord record, UsageEventSnapshot target)
    {
        var score = Math.Abs((record.OccurredAt - target.OccurredAt).TotalSeconds);
        if (target.InputTokens.HasValue && record.InputTokens.HasValue && target.InputTokens.Value != record.InputTokens.Value) score += 120;
        if (string.IsNullOrWhiteSpace(target.Model) || string.IsNullOrWhiteSpace(record.Model)) score += 30;
        return score;
    }

    private static string StripBearer(string value)
        => value.Trim().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value.Trim()[7..].Trim() : value.Trim();

    private static string ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (TryReadProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        return "";
    }

    private static long? ReadLong(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryReadProperty(root, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0) return number;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0) return number;
        }
        return null;
    }

    private static int? ReadInt(JsonElement root, params string[] names) => ReadLong(root, names) is { } value && value <= int.MaxValue ? (int)value : null;

    private static bool TryReadNumber(JsonElement root, out double number, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryReadProperty(root, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number)) return true;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return true;
        }
        number = 0;
        return false;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, params string[] names)
    {
        if (!TryReadNumber(root, out var unix, names))
        {
            foreach (var name in names)
                if (TryReadProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                    return parsed;
            return null;
        }
        try
        {
            return unix > 10_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)unix)
                : DateTimeOffset.FromUnixTimeSeconds((long)unix);
        }
        catch (ArgumentOutOfRangeException) { return null; }
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

    private static bool? ReadBoolean(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        } : null;

    private sealed record PricingCacheEntry(QuotaPricing Pricing, DateTimeOffset UpdatedAt);
    private sealed record QuotaPricing(double Multiplier, string Currency);
}

public sealed record NewApiUsageRecord(
    string Id,
    DateTimeOffset OccurredAt,
    string Model,
    long? InputTokens,
    long? OutputTokens,
    long? CacheReadTokens,
    double RawQuota,
    double Amount,
    string Currency)
{
    public string MatchKey => string.Join('|', Id, OccurredAt.UtcTicks, Model, InputTokens, OutputTokens, RawQuota.ToString("R", CultureInfo.InvariantCulture));
}
