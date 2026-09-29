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
        string? sessionValue = null,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(profile) || (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(sessionValue)))
            return Array.Empty<NewApiUsageRecord>();

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

            var session = string.IsNullOrWhiteSpace(sessionValue)
                && SupportsWebSession(profile)
                ? token
                : sessionValue;
            var pricing = await GetPricingAsync(site, cancellationToken);

            // Prefer a site-specific detail endpoint when the user has
            // supplied the private read-only URL used by the dashboard. This
            // keeps the normal path entirely in the main process; the browser
            // bridge remains only as a fallback for sites that require page
            // JavaScript or a non-replayable request signature.
            var records = await TryFetchConfiguredRecordsAsync(
                site, profile.UsageDetailEndpoint, token, session, pricing, cancellationToken);
            if (records.Count == 0)
            {
            // Prefer a documented/token-authenticated detail API. Some relays
            // expose the New API log endpoint, while newer deployments expose
            // the same records under /api/usage. Probe read-only endpoints in
            // order and only fall back to the web session when none returns
            // usable per-request records.
            records = await TryFetchTokenRecordsAsync(site, token, pricing, cancellationToken);
            if (records.Count == 0)
            {
                // Keep the legacy websee-session mode working for existing
                // profiles, while new profiles can store a separate encrypted
                // session value alongside their API token.
                if (!string.IsNullOrWhiteSpace(session))
                    records = await TryFetchSessionRecordsAsync(site, session, pricing, cancellationToken);
            }
            }

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

    private async Task<IReadOnlyList<NewApiUsageRecord>> TryFetchConfiguredRecordsAsync(
        string site,
        string configuredEndpoint,
        string token,
        string? session,
        QuotaPricing? pricing,
        CancellationToken cancellationToken)
    {
        var endpoint = ResolveConfiguredEndpoint(site, configuredEndpoint);
        if (endpoint is null) return Array.Empty<NewApiUsageRecord>();

        var credentials = new[]
        {
            (Bearer: (string?)null, Session: session),
            (Bearer: string.IsNullOrWhiteSpace(token) ? null : token, Session: (string?)null)
        };
        foreach (var credential in credentials)
        {
            if (string.IsNullOrWhiteSpace(credential.Bearer) && string.IsNullOrWhiteSpace(credential.Session)) continue;
            try
            {
                using var document = await GetJsonAsync(endpoint, credential.Bearer, credential.Session, cancellationToken);
                var records = ParseRecords(document.RootElement, pricing);
                if (records.Count > 0) return records;
            }
            catch (HttpRequestException) { }
            catch (InvalidDataException) { }
            catch (JsonException) { }
        }
        return Array.Empty<NewApiUsageRecord>();
    }

    private static string? ResolveConfiguredEndpoint(string site, string configuredEndpoint)
    {
        if (string.IsNullOrWhiteSpace(configuredEndpoint) || !Uri.TryCreate(site, UriKind.Absolute, out var siteUri)) return null;
        var text = configuredEndpoint.Trim();
        Uri endpoint;
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute)) endpoint = absolute;
        else if (Uri.TryCreate(siteUri, text.StartsWith('/') ? text : "/" + text, out var relative)) endpoint = relative;
        else return null;
        if (endpoint.Scheme != siteUri.Scheme || !string.Equals(endpoint.Host, siteUri.Host, StringComparison.OrdinalIgnoreCase)) return null;
        return endpoint.ToString();
    }

    /// <summary>
    /// Imports JSON responses collected by the browser bridge while the user
    /// was signed in to the relay dashboard. The raw responses are parsed in
    /// memory and only normalized usage records are written to the local,
    /// non-credential usage cache.
    /// </summary>
    public async Task<int> ImportRawResponsesAsync(
        MonitorProfile profile,
        IReadOnlyList<BrowserUsageResponse> responses,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(profile) || responses.Count == 0) return 0;
        var site = BalancePresetCatalog.ResolveSiteUrl(profile);
        if (string.IsNullOrWhiteSpace(site)) return 0;
        var pricing = await GetPricingAsync(site, cancellationToken);
        var parsed = new List<NewApiUsageRecord>();
        string? discoveredEndpoint = null;
        foreach (var response in responses.Take(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(response.Body) || response.Body.Length > 384 * 1024) continue;
            try
            {
                using var document = JsonDocument.Parse(response.Body);
                var responseRecords = ParseRecords(document.RootElement, pricing);
                if (responseRecords.Count > 0 && discoveredEndpoint is null)
                    discoveredEndpoint = SanitizeUsageEndpoint(response.Path);
                parsed.AddRange(responseRecords);
            }
            catch (JsonException) { }
        }
        if (parsed.Count == 0) return 0;
        if (string.IsNullOrWhiteSpace(profile.UsageDetailEndpoint) && !string.IsNullOrWhiteSpace(discoveredEndpoint))
            profile.UsageDetailEndpoint = discoveredEndpoint;
        var cacheKey = $"{profile.Id}|{site.TrimEnd('/')}";
        var merged = MergePersistent(cacheKey, parsed);
        lock (_recentGate) _recentLogs[cacheKey] = new RecentLogCacheEntry(DateTimeOffset.UtcNow, merged);
        return parsed.Count;
    }

    private static string? SanitizeUsageEndpoint(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal) || path.Length > 2048) return null;
        var fragmentIndex = path.IndexOf('#');
        if (fragmentIndex >= 0) path = path[..fragmentIndex];
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0) return path;
        var basePath = path[..queryIndex];
        var safeQuery = path[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var key = part.Split('=', 2)[0].Trim().ToLowerInvariant();
                return key is not ("key" or "token" or "authorization" or "cookie" or "session" or "access_token");
            })
            .ToArray();
        return safeQuery.Length == 0 ? basePath : $"{basePath}?{string.Join('&', safeQuery)}";
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

    public void ReleaseMatches(IEnumerable<NewApiUsageRecord> records)
    {
        lock (_matchedGate)
        {
            foreach (var record in records)
                _matchedLogKeys.Remove(record.MatchKey);
        }
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

    private static bool SupportsWebSession(MonitorProfile profile)
        => string.Equals(profile.AuthMode, "websee-session", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile.AuthMode, "cookie", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile.HeaderName, "Cookie", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<NewApiUsageRecord>> TryFetchTokenRecordsAsync(
        string site,
        string token,
        QuotaPricing? pricing,
        CancellationToken cancellationToken)
    {
        var key = StripBearer(token);
        var endpoints = new[]
        {
            $"{site.TrimEnd('/')}/api/log/token?key={Uri.EscapeDataString(key)}",
            $"{site.TrimEnd('/')}/api/usage?limit=500",
            $"{site.TrimEnd('/')}/api/usage/log?limit=500",
            $"{site.TrimEnd('/')}/api/usage/records?limit=500"
        };
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var document = await GetJsonAsync(endpoint, token, null, cancellationToken);
                var records = ParseRecords(document.RootElement, pricing);
                if (records.Count > 0) return records;
            }
            catch (HttpRequestException) { }
            catch (InvalidDataException) { }
            catch (JsonException) { }
        }
        return Array.Empty<NewApiUsageRecord>();
    }

    private async Task<IReadOnlyList<NewApiUsageRecord>> TryFetchSessionRecordsAsync(
        string site,
        string sessionValue,
        QuotaPricing? pricing,
        CancellationToken cancellationToken)
    {
        var endpoints = new[]
        {
            // The signed-in dashboard uses the current-user log endpoint;
            // unlike /api/log/token it does not require the API key in the
            // query string.
            $"{site.TrimEnd('/')}/api/log/self?p=1&page_size=500&type=2",
            $"{site.TrimEnd('/')}/api/log/self?page_size=500&type=2",
            // Browser-authenticated dashboards commonly expose the same
            // token log without requiring the API key query parameter.
            $"{site.TrimEnd('/')}/api/log/token",
            $"{site.TrimEnd('/')}/api/log/token?limit=500",
            $"{site.TrimEnd('/')}/api/usage?limit=500",
            $"{site.TrimEnd('/')}/api/usage/log?limit=500",
            $"{site.TrimEnd('/')}/api/usage/records?limit=500",
            $"{site.TrimEnd('/')}/usage?format=json"
        };
        foreach (var endpoint in endpoints)
        {
            try
            {
                using var document = await GetJsonAsync(endpoint, null, sessionValue, cancellationToken);
                var records = ParseRecords(document.RootElement, pricing);
                if (records.Count > 0) return records;
            }
            catch (HttpRequestException) { }
            catch (InvalidDataException) { }
            catch (JsonException) { }
        }
        return Array.Empty<NewApiUsageRecord>();
    }

    private async Task<QuotaPricing?> GetPricingAsync(string site, CancellationToken cancellationToken)
    {
        var key = site.TrimEnd('/');
        lock (_pricingGate)
        {
            if (_pricing.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.UpdatedAt < PricingCacheLifetime)
                return cached.Pricing;
        }

        JsonDocument document;
        try
        {
            document = await GetJsonAsync($"{key}/api/status", null, null, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Per-record APIs may return a final currency/amount themselves;
            // lack of the legacy pricing endpoint must not block those APIs.
            return null;
        }
        using (document)
        {
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
    }

    private async Task<JsonDocument> GetJsonAsync(
        string endpoint,
        string? bearerToken,
        string? sessionValue,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(bearerToken));
        if (!string.IsNullOrWhiteSpace(sessionValue))
        {
            var cookie = sessionValue.Trim();
            var userHeader = "";
            var userMarker = cookie.IndexOf("; New-Api-User:", StringComparison.OrdinalIgnoreCase);
            if (userMarker >= 0)
            {
                userHeader = cookie[(userMarker + 15)..].Trim();
                cookie = cookie[..userMarker].Trim();
            }
            if (cookie.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) cookie = cookie[7..].Trim();
            if (cookie.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("Authorization", cookie[14..].Trim());
            else if (cookie.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("Authorization", cookie);
            else if (cookie.Contains('=', StringComparison.Ordinal))
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            else
                request.Headers.TryAddWithoutValidation("Cookie", $"websee-session={cookie}");
            if (!string.IsNullOrWhiteSpace(userHeader) && userHeader.All(char.IsDigit))
                request.Headers.TryAddWithoutValidation("New-Api-User", userHeader);
            request.Headers.Referrer = new Uri(new Uri(endpoint).GetLeftPart(UriPartial.Authority) + "/dashboard");
            request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidDataException("中转站日志响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static IReadOnlyList<NewApiUsageRecord> ParseRecords(JsonElement root, QuotaPricing? pricing)
    {
        if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)
            || (root.ValueKind == JsonValueKind.Object && ReadBoolean(root, "success") == false))
            return Array.Empty<NewApiUsageRecord>();
        var values = EnumerateRecordValues(root);

        var records = new List<NewApiUsageRecord>();
        foreach (var value in values)
        {
            if (value.ValueKind != JsonValueKind.Object) continue;
            var type = ReadInt(value, "type");
            if (type.HasValue && type.Value != 2) continue;
            var hasQuota = TryReadNumber(value, out var quota,
                "quota", "raw_quota", "rawQuota", "used_quota", "usedQuota", "total_quota",
                "usage.quota", "usage.raw_quota", "usage.used_quota");
            var hasAmount = TryReadNumber(value, out var amount,
                "cost", "amount", "fee", "price", "actual_cost", "actualCost", "spend",
                "total_cost", "cost_usd", "price_usd", "amount_usd", "charge", "total_charge",
                "usage.cost", "usage.amount", "usage.total_cost", "usage.charge");
            if (!hasQuota && !hasAmount) continue;
            if (hasQuota && (quota < 0 || !double.IsFinite(quota))) continue;
            var createdAt = ReadTimestamp(value, "created_at", "createdAt", "timestamp", "created", "time", "request_time", "updated_at", "usage.created_at", "usage.timestamp");
            if (!createdAt.HasValue) continue;
            var model = ReadString(value, "model_name", "modelName", "model", "usage.model", "usage.model_name").Trim();
            var currency = ReadString(value, "currency", "currency_code", "unit", "currency_symbol").Trim();
            var multiplier = pricing?.Multiplier ?? 1d;
            var resolvedAmount = hasAmount ? amount : quota * multiplier;
            var resolvedQuota = hasQuota ? quota : (multiplier > 0 ? amount / multiplier : amount);
            if (!double.IsFinite(resolvedAmount) || resolvedAmount < 0) continue;
            if (string.IsNullOrWhiteSpace(currency)) currency = pricing?.Currency ?? "USD";
            records.Add(new NewApiUsageRecord(
                ReadString(value, "id", "request_id", "requestId", "request_id_hash", "usage.request_id").Trim(),
                createdAt.Value,
                model,
                ReadLong(value, "prompt_tokens", "input_tokens", "inputTokens", "usage.prompt_tokens", "usage.input_tokens"),
                ReadLong(value, "completion_tokens", "output_tokens", "outputTokens", "usage.completion_tokens", "usage.output_tokens"),
                ReadLong(value, "cached_tokens", "cache_read_tokens", "cacheReadTokens", "usage.cached_tokens", "usage.cache_read_tokens"),
                resolvedQuota,
                resolvedAmount,
                currency,
                NormalizeReasoning(ReadString(value, "reasoning_effort", "reasoningEffort", "reasoning_level", "reasoningLevel", "thinking_level", "thinkingLevel", "effort", "usage.reasoning_effort"))));
        }
        return records;
    }

    private static IEnumerable<JsonElement> EnumerateRecordValues(JsonElement root)
    {
        var candidates = new[] { "data", "items", "records", "logs", "results", "rows", "list", "usage" };
        foreach (var path in candidates)
        {
            if (!TryReadProperty(root, path, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray();
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in new[] { "items", "records", "logs", "results", "data", "rows", "list" })
                    if (value.TryGetProperty(nested, out var array) && array.ValueKind == JsonValueKind.Array)
                        return array.EnumerateArray();
                if (LooksLikeRecord(value)) return new[] { value };
            }
        }
        return root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }

    private static bool LooksLikeRecord(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
            && (value.TryGetProperty("created_at", out _)
                || value.TryGetProperty("createdAt", out _)
                || value.TryGetProperty("timestamp", out _)
                || value.TryGetProperty("cost", out _)
                || value.TryGetProperty("quota", out _));

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
