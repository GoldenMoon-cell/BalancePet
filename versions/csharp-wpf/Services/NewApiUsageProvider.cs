using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Reads the read-only per-token log exposed by New API compatible relays.
/// The API key is sent only to the configured relay host and is never included
/// in exceptions, logs, or persisted usage data.
/// </summary>
public sealed class NewApiUsageProvider(HttpClient http, string? dataDirectory = null)
{
    private const int MaxLogItems = 5000;
    private const long MaxResponseBytes = 8L * 1024 * 1024;
    private static readonly TimeSpan PricingCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RecentLogCacheLifetime = TimeSpan.FromSeconds(4);
    private readonly object _pricingGate = new();
    private readonly Dictionary<string, PricingCacheEntry> _pricing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _recentGate = new();
    private readonly Dictionary<string, RecentLogCacheEntry> _recentLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _recentFetchGate = new(1, 1);
    private readonly object _matchedGate = new();
    private readonly HashSet<string> _matchedLogKeys = new(StringComparer.Ordinal);
    private readonly string _persistentCachePath = Path.Combine(
        dataDirectory ?? UsageEventBridge.GetDefaultDirectory(), "relay-usage-cache.v1.json");
    private readonly object _persistentCacheGate = new();
    private readonly Dictionary<string, PersistentCacheEntry> _persistentCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _persistentCacheLoaded;

    public string CachePath => _persistentCachePath;

    public void ClearCache()
    {
        lock (_persistentCacheGate)
        {
            _persistentCache.Clear();
            _persistentCacheLoaded = true;
            try { if (File.Exists(_persistentCachePath)) File.Delete(_persistentCachePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        lock (_recentGate) _recentLogs.Clear();
    }

    public async Task<IReadOnlyList<NewApiUsageRecord>> FetchRecentAsync(
        MonitorProfile profile,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(profile) || string.IsNullOrWhiteSpace(token)) return Array.Empty<NewApiUsageRecord>();

        var site = BalancePresetCatalog.ResolveSiteUrl(profile);
        if (string.IsNullOrWhiteSpace(site)) return Array.Empty<NewApiUsageRecord>();
        var cacheKey = $"{profile.Id}|{site.TrimEnd('/')}";
        var persistent = GetPersistentRecords(cacheKey);
        lock (_recentGate)
        {
            if (_recentLogs.TryGetValue(cacheKey, out var cached)
                && DateTimeOffset.UtcNow - cached.FetchedAt < RecentLogCacheLifetime)
                return cached.Records;
        }

        await _recentFetchGate.WaitAsync(cancellationToken);
        try
        {
            lock (_recentGate)
            {
                if (_recentLogs.TryGetValue(cacheKey, out var cached)
                    && DateTimeOffset.UtcNow - cached.FetchedAt < RecentLogCacheLifetime)
                    return cached.Records;
            }

            var pricing = await GetPricingAsync(site, cancellationToken);
            if (pricing is null)
            {
                lock (_recentGate) _recentLogs[cacheKey] = new RecentLogCacheEntry(DateTimeOffset.UtcNow, persistent);
                return persistent;
            }

            // New API deployments in the wild use both authentication forms:
            // the documented read-only `key` query and bearer middleware. Send
            // both so older compatible relays and newer relays behave alike;
            // the token is never logged or persisted by BalancePet.
            var endpoint = $"{site.TrimEnd('/')}/api/log/token?key={Uri.EscapeDataString(StripBearer(token))}";
            using var document = await GetJsonAsync(endpoint, token, cancellationToken);
            var records = ParseRecords(document.RootElement, pricing);
            var result = MergePersistent(cacheKey, records);
            lock (_recentGate) _recentLogs[cacheKey] = new RecentLogCacheEntry(DateTimeOffset.UtcNow, result);
            return result;
        }
        catch (HttpRequestException) when (persistent.Count > 0)
        {
            lock (_recentGate) _recentLogs[cacheKey] = new RecentLogCacheEntry(DateTimeOffset.UtcNow, persistent);
            return persistent;
        }
        catch (InvalidDataException) when (persistent.Count > 0)
        {
            lock (_recentGate) _recentLogs[cacheKey] = new RecentLogCacheEntry(DateTimeOffset.UtcNow, persistent);
            return persistent;
        }
        catch (JsonException) when (persistent.Count > 0)
        {
            lock (_recentGate) _recentLogs[cacheKey] = new RecentLogCacheEntry(DateTimeOffset.UtcNow, persistent);
            return persistent;
        }
        finally
        {
            _recentFetchGate.Release();
        }
    }

    private IReadOnlyList<NewApiUsageRecord> GetPersistentRecords(string cacheKey)
    {
        lock (_persistentCacheGate)
        {
            EnsurePersistentCacheLoaded();
            return _persistentCache.TryGetValue(cacheKey, out var entry)
                ? entry.Records
                : Array.Empty<NewApiUsageRecord>();
        }
    }

    private IReadOnlyList<NewApiUsageRecord> MergePersistent(string cacheKey, IReadOnlyList<NewApiUsageRecord> fresh)
    {
        lock (_persistentCacheGate)
        {
            EnsurePersistentCacheLoaded();
            var now = DateTimeOffset.UtcNow;
            var merged = (_persistentCache.TryGetValue(cacheKey, out var old) ? old.Records : Array.Empty<NewApiUsageRecord>())
                .Concat(fresh)
                .Where(record => record.OccurredAt >= now - TimeSpan.FromDays(30))
                .GroupBy(record => record.MatchKey, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(record => record.OccurredAt).First())
                .OrderByDescending(record => record.OccurredAt)
                .Take(MaxLogItems)
                .ToArray();
            _persistentCache[cacheKey] = new PersistentCacheEntry(cacheKey, now, merged);
            SavePersistentCache();
            return merged;
        }
    }

    private void EnsurePersistentCacheLoaded()
    {
        if (_persistentCacheLoaded) return;
        _persistentCacheLoaded = true;
        try
        {
            if (!File.Exists(_persistentCachePath)) return;
            using var stream = File.OpenRead(_persistentCachePath);
            var envelope = JsonSerializer.Deserialize<PersistentCacheEnvelope>(stream);
            foreach (var entry in envelope?.Entries ?? Array.Empty<PersistentCacheEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                _persistentCache[entry.Key] = entry with
                {
                    Records = entry.Records.Where(record => record.OccurredAt >= DateTimeOffset.UtcNow - TimeSpan.FromDays(30)).Take(MaxLogItems).ToArray()
                };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    private void SavePersistentCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_persistentCachePath)!);
            var temp = _persistentCachePath + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, new PersistentCacheEnvelope(_persistentCache.Values.ToArray()));
            File.Move(temp, _persistentCachePath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

    public bool TryTakeDetailMatches(
        IReadOnlyList<NewApiUsageRecord> records,
        IReadOnlyList<UsageEventDetailSnapshot> details,
        out IReadOnlyList<NewApiUsageRecord> matches)
    {
        var result = new List<NewApiUsageRecord>();
        foreach (var detail in details.Where(value => value.OccurredAt != DateTimeOffset.MinValue))
        {
            var target = new UsageEventSnapshot(
                "detail",
                detail.OccurredAt,
                "Codex",
                detail.Model,
                detail.InputTokens,
                detail.OutputTokens,
                detail.CacheReadTokens,
                null,
                null,
                detail.Currency,
                null,
                null,
                null,
                null,
                true,
                detail.ReasoningEffort);
            if (TryTakeMatch(records, target, TimeSpan.FromMinutes(30), out var match) && match is not null)
                result.Add(match);
        }

        matches = result;
        return result.Count > 0;
    }

    public bool TryTakeTaskMatches(IReadOnlyList<NewApiUsageRecord> records, UsageEventSnapshot target, DateTimeOffset? startedAt, out IReadOnlyList<NewApiUsageRecord> matches)
    {
        matches = Array.Empty<NewApiUsageRecord>();
        if (!startedAt.HasValue || startedAt.Value >= target.OccurredAt || !target.OutputTokens.HasValue) return false;
        var candidates = records
            .Where(record => record.OccurredAt >= startedAt.Value - TimeSpan.FromSeconds(10) && record.OccurredAt <= target.OccurredAt + TimeSpan.FromSeconds(10))
            .Where(record => string.IsNullOrWhiteSpace(target.Model) || string.IsNullOrWhiteSpace(record.Model) || string.Equals(record.Model, target.Model, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.OccurredAt).ToArray();
        if (candidates.Length == 0 && !string.IsNullOrWhiteSpace(target.Model))
        {
            // Some relays expose the upstream model name instead of the model
            // requested by the client. Time and aggregate output are still
            // strong match signals, so retry without the model filter.
            candidates = records
                .Where(record => record.OccurredAt >= startedAt.Value - TimeSpan.FromSeconds(10) && record.OccurredAt <= target.OccurredAt + TimeSpan.FromSeconds(10))
                .OrderBy(record => record.OccurredAt).ToArray();
        }
        // A single Codex task can generate hundreds of relay requests.  The
        // detail snapshot is capped separately when it is persisted, but the
        // cost matcher must still be allowed to sum the complete task.
        if (candidates.Length is 0 or > 1000) return false;
        lock (_matchedGate)
        {
            candidates = candidates.Where(record => !_matchedLogKeys.Contains(record.MatchKey)).ToArray();
            if (candidates.Length == 0 || candidates.Any(record => !record.OutputTokens.HasValue)) return false;
            var totalOutput = candidates.Sum(record => record.OutputTokens!.Value);
            if (Math.Abs(totalOutput - target.OutputTokens.Value) > Math.Max(64d, target.OutputTokens.Value * 0.03d)) return false;
            foreach (var record in candidates) _matchedLogKeys.Add(record.MatchKey);
            matches = candidates;
            return matches.Count > 0;
        }
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

        using var document = await GetJsonAsync($"{key}/api/status", null, cancellationToken);
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

    private async Task<JsonDocument> GetJsonAsync(string endpoint, string? bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(bearerToken));
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
                pricing.Currency,
                NormalizeReasoning(ReadString(value, "reasoning_effort", "reasoningEffort", "reasoning_level", "reasoningLevel", "thinking_level", "thinkingLevel", "effort"))));
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

    private static string NormalizeReasoning(string? value)
    {
        var cleaned = (value ?? "").Trim();
        return cleaned.Length <= 32 ? cleaned : cleaned[..32];
    }

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
    private sealed record RecentLogCacheEntry(DateTimeOffset FetchedAt, IReadOnlyList<NewApiUsageRecord> Records);
    private sealed record PersistentCacheEnvelope(IReadOnlyList<PersistentCacheEntry> Entries);
    private sealed record PersistentCacheEntry(string Key, DateTimeOffset FetchedAt, IReadOnlyList<NewApiUsageRecord> Records);
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
    string Currency,
    string ReasoningEffort = "")
{
    public string MatchKey => string.Join('|', Id, OccurredAt.UtcTicks, Model, InputTokens, OutputTokens, RawQuota.ToString("R", CultureInfo.InvariantCulture));
}
