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
    public Task RecordAsync(
        string provider,
        string? model = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? cacheReadTokens = null,
        long? cacheWriteTokens = null,
        long? durationMs = null,
        long? timeToFirstTokenMs = null,
        long? toolCalls = null,
        long? steps = null,
        bool? success = null,
        CancellationToken cancellationToken = default)
    {
        var sanitizedProvider = Clean(provider, 64);
        if (sanitizedProvider.Length == 0) return Task.CompletedTask;
        return AppendAsync(new UsageEventData
        {
            Schema = "balancepet.usage.v1",
            EventId = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.Now,
            Kind = "llm_request",
            Provider = sanitizedProvider,
            Model = Clean(model, 160),
            Success = success,
            InputTokens = ClampCounter(inputTokens),
            OutputTokens = ClampCounter(outputTokens),
            CacheReadTokens = ClampCounter(cacheReadTokens),
            CacheWriteTokens = ClampCounter(cacheWriteTokens),
            DurationMs = ClampCounter(durationMs),
            TimeToFirstTokenMs = ClampCounter(timeToFirstTokenMs),
            ToolCalls = ClampCounter(toolCalls),
            Steps = ClampCounter(steps)
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

    private async Task AppendAsync(UsageEventData usageEvent, CancellationToken cancellationToken)
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
        public long? DurationMs { get; set; }
        public long? TimeToFirstTokenMs { get; set; }
        public long? ToolCalls { get; set; }
        public long? Steps { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
