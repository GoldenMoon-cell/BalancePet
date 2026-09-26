using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Receives sanitized usage counters from local clients. The protocol has no
/// fields for prompts, responses, cookies, API keys, or arbitrary payloads.
/// </summary>
public sealed class UsageEventBridge : IDisposable
{
    public const string PipeName = "BalancePet.Usage.v1";
    private const int MaxMessageLength = 16_384;
    private const int MaxStoredLineLength = 128 * 1024;
    private const long MaxFileBytes = 100L * 1024 * 1024;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _listener;

    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet");

    public static string GetEventPath() => Path.Combine(GetDefaultDirectory(), "usage-events.ndjson");

    /// <summary>
    /// Records a sanitized usage sample supplied by a local client adapter.
    /// Only counters and timings are accepted; callers cannot pass prompts,
    /// responses, credentials, or arbitrary metadata through this API.
    /// </summary>
    public Task<string?> RecordAsync(
        string provider,
        string? model = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? cacheReadTokens = null,
        long? cacheWriteTokens = null,
        double? cost = null,
        string? currency = null,
        long? durationMs = null,
        long? timeToFirstTokenMs = null,
        long? toolCalls = null,
        long? steps = null,
        bool? success = null,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null,
        IReadOnlyList<UsageEventDetailSnapshot>? details = null)
    {
        return RecordCoreAsync(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.Now,
            provider,
            model,
            inputTokens,
            outputTokens,
            cacheReadTokens,
            cacheWriteTokens,
            cost,
            currency,
            durationMs,
            timeToFirstTokenMs,
            toolCalls,
            steps,
            success,
            reasoningEffort,
            details,
            cancellationToken);
    }

    public Task<string?> UpdateAsync(
        string eventId,
        DateTimeOffset occurredAt,
        string provider,
        string? model = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? cacheReadTokens = null,
        long? cacheWriteTokens = null,
        double? cost = null,
        string? currency = null,
        long? durationMs = null,
        long? timeToFirstTokenMs = null,
        long? toolCalls = null,
        long? steps = null,
        bool? success = null,
        CancellationToken cancellationToken = default,
        string? reasoningEffort = null,
        IReadOnlyList<UsageEventDetailSnapshot>? details = null)
    {
        return RecordCoreAsync(
            Clean(eventId, 80),
            occurredAt,
            provider,
            model,
            inputTokens,
            outputTokens,
            cacheReadTokens,
            cacheWriteTokens,
            cost,
            currency,
            durationMs,
            timeToFirstTokenMs,
            toolCalls,
            steps,
            success,
            reasoningEffort,
            details,
            cancellationToken);
    }

    private Task<string?> RecordCoreAsync(
        string eventId,
        DateTimeOffset occurredAt,
        string provider,
        string? model,
        long? inputTokens,
        long? outputTokens,
        long? cacheReadTokens,
        long? cacheWriteTokens,
        double? cost,
        string? currency,
        long? durationMs,
        long? timeToFirstTokenMs,
        long? toolCalls,
        long? steps,
        bool? success,
        string? reasoningEffort,
        IReadOnlyList<UsageEventDetailSnapshot>? details,
        CancellationToken cancellationToken)
    {
        var sanitizedProvider = Clean(provider, 64);
        if (sanitizedProvider.Length == 0 || string.IsNullOrWhiteSpace(eventId)) return Task.FromResult<string?>(null);
        return AppendAsync(new UsageEventData
        {
            Schema = "balancepet.usage.v1",
            EventId = eventId,
            OccurredAt = occurredAt,
            Kind = "llm_request",
            Provider = sanitizedProvider,
            Model = Clean(model, 160),
            Success = success,
            InputTokens = ClampCounter(inputTokens),
            OutputTokens = ClampCounter(outputTokens),
            CacheReadTokens = ClampCounter(cacheReadTokens),
            CacheWriteTokens = ClampCounter(cacheWriteTokens),
            Cost = ClampAmount(cost),
            Currency = Clean(currency, 12).ToUpperInvariant(),
            DurationMs = ClampCounter(durationMs),
            TimeToFirstTokenMs = ClampCounter(timeToFirstTokenMs),
            ToolCalls = ClampCounter(toolCalls),
            Steps = ClampCounter(steps),
            ReasoningEffort = Clean(reasoningEffort, 32),
            Details = details?.Take(192).Select(detail => detail.Sanitized()).ToArray() ?? Array.Empty<UsageEventDetailSnapshot>()
        }, cancellationToken);
    }

    public void Start()
    {
        if (_listener is { IsCompleted: false }) return;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _listener = ListenAsync(_cancellation.Token);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _listener = null;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 4, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await ProcessClientAsync(pipe, cancellationToken);
                pipe = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Delay(250, cancellationToken);
            }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task ProcessClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line) || line.Length > MaxMessageLength) return;
                var usageEvent = Parse(line);
                if (usageEvent is not null) await AppendAsync(usageEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    private async Task<string?> AppendAsync(UsageEventData usageEvent, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var directory = GetDefaultDirectory();
            Directory.CreateDirectory(directory);
            var path = GetEventPath();
            RotateIfNeeded(path);
            var line = JsonSerializer.Serialize(usageEvent, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line, new UTF8Encoding(false), cancellationToken);
            return usageEvent.EventId;
        }
        finally { _writeLock.Release(); }
    }

    private static UsageEventData? Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;
        var provider = Clean(ReadString(root, "provider", "source"), 64);
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var kind = Clean(ReadString(root, "kind", "event", "type"), 32);
        if (!string.IsNullOrWhiteSpace(kind) && kind != "llm_request") return null;
        var occurredAt = ReadDate(root, "occurred_at", "occurredAt", "timestamp") ?? DateTimeOffset.Now;
        var usage = FindUsageObject(root);
        return new UsageEventData
        {
            Schema = "balancepet.usage.v1",
            EventId = Clean(ReadString(root, "event_id", "eventId", "id"), 80) is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N"),
            OccurredAt = occurredAt,
            Kind = "llm_request",
            Provider = provider,
            AccountId = Clean(ReadString(root, "account_id", "accountId"), 128),
            Model = Clean(ReadString(root, "model") ?? ReadString(usage, "model"), 160),
            Success = ReadBool(usage, "success") ?? ReadBool(root, "success"),
            InputTokens = ReadCounter(usage, "input_tokens", "inputTokens", "prompt_tokens", "promptTokens", "input_token_count", "inputTokenCount", "prompt_token_count", "promptTokenCount") ?? ReadCounter(root, "input_tokens", "inputTokens", "prompt_tokens", "promptTokens", "input_token_count", "inputTokenCount", "prompt_token_count", "promptTokenCount"),
            OutputTokens = ReadCounter(usage, "output_tokens", "outputTokens", "completion_tokens", "completionTokens", "output_token_count", "outputTokenCount", "candidates_token_count", "candidatesTokenCount", "completion_token_count", "completionTokenCount") ?? ReadCounter(root, "output_tokens", "outputTokens", "completion_tokens", "completionTokens", "output_token_count", "outputTokenCount", "candidates_token_count", "candidatesTokenCount", "completion_token_count", "completionTokenCount"),
            CacheReadTokens = ReadCounter(usage, "cache_read_tokens", "cacheReadTokens", "cached_tokens", "cachedTokens", "cache_read_input_tokens", "cacheReadInputTokens", "cache_hit_tokens", "cacheHitTokens") ?? ReadCounter(root, "cache_read_tokens", "cacheReadTokens", "cached_tokens", "cachedTokens", "cache_read_input_tokens", "cacheReadInputTokens", "cache_hit_tokens", "cacheHitTokens"),
            CacheWriteTokens = ReadCounter(usage, "cache_write_tokens", "cacheWriteTokens", "cache_creation_input_tokens", "cacheCreationInputTokens") ?? ReadCounter(root, "cache_write_tokens", "cacheWriteTokens", "cache_creation_input_tokens", "cacheCreationInputTokens"),
            Cost = ReadAmount(usage, "cost", "amount", "usage_cost", "usageCost") ?? ReadAmount(root, "cost", "amount", "usage_cost", "usageCost"),
            Currency = Clean(ReadString(usage, "currency", "cost_currency", "costCurrency") ?? ReadString(root, "currency", "cost_currency", "costCurrency"), 12).ToUpperInvariant(),
            DurationMs = ReadCounter(root, "duration_ms", "durationMs", "elapsed_ms", "elapsedMs") ?? ReadCounter(usage, "duration_ms", "durationMs", "elapsed_ms", "elapsedMs"),
            TimeToFirstTokenMs = ReadCounter(usage, "time_to_first_token_ms", "timeToFirstTokenMs", "ttft_ms", "time_to_first_token", "timeToFirstToken") ?? ReadCounter(root, "time_to_first_token_ms", "timeToFirstTokenMs", "ttft_ms", "time_to_first_token", "timeToFirstToken"),
            ToolCalls = ReadCounter(usage, "tool_calls", "toolCalls") ?? ReadCounter(root, "tool_calls", "toolCalls"),
            Steps = ReadCounter(usage, "steps") ?? ReadCounter(root, "steps")
        };
    }

    private static long? ReadCounter(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number is >= 0 and <= 10_000_000_000 ? number : null;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number is >= 0 and <= 10_000_000_000 ? number : null;
        }
        return null;
    }

    private static double? ReadAmount(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return ClampAmount(number);
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return ClampAmount(number);
        }
        return null;
    }

    private static JsonElement FindUsageObject(JsonElement root)
    {
        foreach (var name in new[] { "usage", "token_usage", "tokenUsage", "usage_metadata", "usageMetadata", "response", "result", "metadata", "stats", "metrics" })
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) continue;
            if (value.TryGetProperty("usage", out var nested) && nested.ValueKind == JsonValueKind.Object) return nested;
            if (value.TryGetProperty("token_usage", out nested) && nested.ValueKind == JsonValueKind.Object) return nested;
            if (value.TryGetProperty("tokenUsage", out nested) && nested.ValueKind == JsonValueKind.Object) return nested;
            if (HasUsageField(value)) return value;
        }
        return root;
    }

    private static bool HasUsageField(JsonElement value)
        => new[] { "input_tokens", "inputTokens", "prompt_tokens", "promptTokenCount", "output_tokens", "outputTokens", "completion_tokens", "candidatesTokenCount", "model" }
            .Any(name => value.TryGetProperty(name, out _));

    private static bool? ReadBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return parsed;
        }
        return null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names) if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        return null;
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }

    private static long? ClampCounter(long? value) => value is >= 0 and <= 10_000_000_000 ? value : null;
    private static double? ClampAmount(double? value) => value.HasValue && double.IsFinite(value.Value) && value.Value is >= 0 and <= 10_000_000_000 ? value : null;

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes) return;
            var rotated = Path.Combine(Path.GetDirectoryName(path)!, $"usage-events-{DateTime.UtcNow:yyyyMMddHHmmssfff}.ndjson");
            File.Move(path, rotated, true);
        }
        catch (IOException) { }
    }

    public IReadOnlyList<UsageEventSnapshot> ReadRecentEvents(int maxEvents = 500)
    {
        var limit = Math.Clamp(maxEvents, 1, 5000);
        if (!Directory.Exists(GetDefaultDirectory())) return Array.Empty<UsageEventSnapshot>();

        var events = new Dictionary<string, UsageEventSnapshot>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(GetDefaultDirectory(), "usage-events*.ndjson", SearchOption.TopDirectoryOnly)
                .Select(path => new { Path = path, LastWrite = File.GetLastWriteTimeUtc(path) })
                .OrderByDescending(value => value.LastWrite)
                .Take(32)
                .OrderBy(value => value.LastWrite)
                .Select(value => value.Path)
                .ToArray();
        }
        catch (IOException) { return Array.Empty<UsageEventSnapshot>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<UsageEventSnapshot>(); }

        foreach (var path in files)
        {
            try
            {
                using var reader = new StreamReader(path);
                while (events.Count < limit * 2 && reader.ReadLine() is { } line)
                {
                    if (line.Length == 0 || line.Length > MaxStoredLineLength) continue;
                    if (!TryReadSnapshot(line, out var snapshot) || snapshot is null) continue;
                    events[snapshot.EventId] = snapshot;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return events.Values
            .OrderByDescending(value => value.OccurredAt)
            .Take(limit)
            .ToArray();
    }

    private static bool TryReadSnapshot(string line, out UsageEventSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!string.Equals(ReadString(root, "schema"), "balancepet.usage.v1", StringComparison.Ordinal)
                || !string.Equals(ReadString(root, "kind"), "llm_request", StringComparison.Ordinal))
                return false;
            var eventId = Clean(ReadString(root, "event_id", "eventId", "id"), 80);
            var provider = Clean(ReadString(root, "provider", "source"), 64);
            if (eventId.Length == 0 || provider.Length == 0) return false;
            snapshot = new UsageEventSnapshot(
                eventId,
                ReadDate(root, "occurred_at", "occurredAt", "timestamp") ?? DateTimeOffset.MinValue,
                provider,
                Clean(ReadString(root, "model"), 160),
                ReadCounter(root, "input_tokens", "inputTokens"),
                ReadCounter(root, "output_tokens", "outputTokens"),
                ReadCounter(root, "cache_read_tokens", "cacheReadTokens"),
                ReadCounter(root, "cache_write_tokens", "cacheWriteTokens"),
                ReadAmount(root, "cost", "amount", "usage_cost", "usageCost"),
                Clean(ReadString(root, "currency", "cost_currency", "costCurrency"), 12).ToUpperInvariant(),
                ReadCounter(root, "duration_ms", "durationMs", "elapsed_ms", "elapsedMs"),
                ReadCounter(root, "time_to_first_token_ms", "timeToFirstTokenMs", "ttft_ms", "timeToFirstToken"),
                ReadCounter(root, "tool_calls", "toolCalls"),
                ReadCounter(root, "steps"),
                ReadBool(root, "success"),
                Clean(ReadString(root, "reasoning_effort", "reasoningEffort"), 32),
                ReadDetails(root));
            return snapshot.OccurredAt != DateTimeOffset.MinValue;
        }
        catch (JsonException) { return false; }
    }

    private static IReadOnlyList<UsageEventDetailSnapshot> ReadDetails(JsonElement root)
    {
        if (!root.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            return Array.Empty<UsageEventDetailSnapshot>();
        var result = new List<UsageEventDetailSnapshot>();
        foreach (var item in details.EnumerateArray().Take(192))
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            result.Add(new UsageEventDetailSnapshot(
                ReadDate(item, "occurred_at", "occurredAt") ?? DateTimeOffset.MinValue,
                Clean(ReadString(item, "model"), 160),
                Clean(ReadString(item, "reasoning_effort", "reasoningEffort"), 32),
                ReadCounter(item, "input_tokens", "inputTokens"),
                ReadCounter(item, "output_tokens", "outputTokens"),
                ReadCounter(item, "cache_read_tokens", "cacheReadTokens"),
                ReadAmount(item, "cost", "amount"),
                Clean(ReadString(item, "currency"), 12).ToUpperInvariant()));
        }
        return result;
    }

    public void Dispose()
    {
        Stop();
        _writeLock.Dispose();
    }

    private sealed class UsageEventData
    {
        public string Schema { get; set; } = "balancepet.usage.v1";
        public string EventId { get; set; } = "";
        public DateTimeOffset OccurredAt { get; set; }
        public string Kind { get; set; } = "llm_request";
        public string Provider { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string Model { get; set; } = "";
        public bool? Success { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? CacheReadTokens { get; set; }
        public long? CacheWriteTokens { get; set; }
        public double? Cost { get; set; }
        public string Currency { get; set; } = "";
        public long? DurationMs { get; set; }
        public long? TimeToFirstTokenMs { get; set; }
        public long? ToolCalls { get; set; }
        public long? Steps { get; set; }
        public string ReasoningEffort { get; set; } = "";
        public IReadOnlyList<UsageEventDetailSnapshot> Details { get; set; } = Array.Empty<UsageEventDetailSnapshot>();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record UsageEventSnapshot(
    string EventId,
    DateTimeOffset OccurredAt,
    string Provider,
    string Model,
    long? InputTokens,
    long? OutputTokens,
    long? CacheReadTokens,
    long? CacheWriteTokens,
    double? Cost,
    string Currency,
    long? DurationMs,
    long? TimeToFirstTokenMs,
    long? ToolCalls,
    long? Steps,
    bool? Success,
    string ReasoningEffort = "",
    IReadOnlyList<UsageEventDetailSnapshot>? Details = null);

public sealed record UsageEventDetailSnapshot(
    DateTimeOffset OccurredAt,
    string Model,
    string ReasoningEffort,
    long? InputTokens,
    long? OutputTokens,
    long? CacheReadTokens,
    double? Cost,
    string Currency)
{
    public UsageEventDetailSnapshot Sanitized() => this with
    {
        Model = Model.Length > 160 ? Model[..160] : Model,
        ReasoningEffort = ReasoningEffort.Length > 32 ? ReasoningEffort[..32] : ReasoningEffort,
        Currency = Currency.Length > 12 ? Currency[..12] : Currency,
        InputTokens = Clamp(InputTokens),
        OutputTokens = Clamp(OutputTokens),
        CacheReadTokens = Clamp(CacheReadTokens),
        Cost = Cost is { } cost && double.IsFinite(cost) && cost >= 0 && cost <= 10_000_000_000 ? cost : null
    };

    private static long? Clamp(long? value) => value is >= 0 and <= 10_000_000_000 ? value : null;
}
