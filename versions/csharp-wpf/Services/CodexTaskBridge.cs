using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace BalancePet.Wpf.Services;

public sealed record CodexTaskActivity(string State, string SessionId, string TurnId)
{
    public string Key => $"{SessionId}:{TurnId}";
    public string Provider { get; init; } = "Codex";
    public string Model { get; init; } = "";
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? CacheReadTokens { get; init; }
    public long? CacheWriteTokens { get; init; }
    public double? Cost { get; init; }
    public string Currency { get; init; } = "";
    public long? DurationMs { get; init; }
    public long? TimeToFirstTokenMs { get; init; }
    public long? ToolCalls { get; init; }
    public long? Steps { get; init; }
    public string ReasoningEffort { get; init; } = "";
    public bool? Success { get; init; }
}

public sealed class CodexTaskBridge : IDisposable
{
    // Keep the original pipe for existing Codex hooks. Other clients should
    // use the provider-neutral pipe below or the bundled sender script.
    public const string PipeName = "BalancePet.CodexTask.v1";
    public const string GenericPipeName = "BalancePet.Task.v1";

    private CancellationTokenSource? _cancellation;
    private Task[] _listeners = Array.Empty<Task>();
    private readonly object _pendingLock = new();
    private readonly Dictionary<string, CodexTaskActivity> _pendingStarts = new(StringComparer.Ordinal);

    public event EventHandler<CodexTaskActivity>? ActivityReceived;

    public void Start()
    {
        if (_listeners.Any(listener => !listener.IsCompleted)) return;
        _cancellation = new CancellationTokenSource();
        _listeners =
        [
            ListenAsync(PipeName, "Codex", _cancellation.Token),
            ListenAsync(GenericPipeName, "其他客户端", _cancellation.Token)
        ];
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _listeners = Array.Empty<Task>();
        lock (_pendingLock) _pendingStarts.Clear();
    }

    private async Task ListenAsync(string pipeName, string defaultProvider, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    8,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await ProcessClientAsync(pipe, defaultProvider, cancellationToken);
                pipe = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Delay(250, cancellationToken);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task ProcessClientAsync(NamedPipeServerStream pipe, string defaultProvider, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line) || line.Length > 4096) return;

                var activity = ParseActivity(line, defaultProvider);
                if (activity is null || activity.State is not ("start" or "stop")) return;
                if (activity.State == "start" && string.IsNullOrWhiteSpace(activity.SessionId)) return;
                // Some Codex Stop payloads may omit turn_id; the main window can
                // still match that completion to the active task's session.
                if (activity.State == "start" && string.IsNullOrWhiteSpace(activity.TurnId)) return;
                ActivityReceived?.Invoke(this, MergeWithStart(activity));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    private CodexTaskActivity MergeWithStart(CodexTaskActivity activity)
    {
        if (activity.State == "start")
        {
            lock (_pendingLock)
            {
                if (_pendingStarts.Count >= 2048) _pendingStarts.Clear();
                _pendingStarts[activity.Key] = activity;
            }
            return activity;
        }

        CodexTaskActivity? started = null;
        lock (_pendingLock)
        {
            var hasTurn = HasIdentity(activity.TurnId);
            var hasSession = HasIdentity(activity.SessionId) && !IsExternalSession(activity.SessionId);
            if (hasTurn && hasSession && _pendingStarts.Remove(activity.Key, out var exact))
                started = exact;
            else if (hasTurn)
            {
                // A stop hook can preserve turn_id but omit or rotate the
                // session id. Match the unique pending start by turn id.
                var matchingKey = _pendingStarts.Keys.FirstOrDefault(key =>
                    key.EndsWith($":{activity.TurnId}", StringComparison.Ordinal));
                if (matchingKey is not null) _pendingStarts.Remove(matchingKey, out started);
            }
            else if (hasSession)
            {
                var prefix = activity.SessionId + ":";
                var match = _pendingStarts.Keys.FirstOrDefault(key => key.StartsWith(prefix, StringComparison.Ordinal));
                if (match is not null) _pendingStarts.Remove(match, out started);
            }
            else if (_pendingStarts.Count > 0)
            {
                // When Stop receives no stdin at all, the host still expects
                // one completion event. The newest pending start is the only
                // identity available and is preferable to a count-only record.
                var newestKey = _pendingStarts.Keys.Last();
                _pendingStarts.Remove(newestKey, out started);
            }
        }

        if (started is null) return activity;
        return activity with
        {
            SessionId = started.SessionId,
            TurnId = started.TurnId,
            Provider = string.IsNullOrWhiteSpace(activity.Provider) ? started.Provider : activity.Provider,
            Model = Prefer(activity.Model, started.Model),
            InputTokens = activity.InputTokens ?? started.InputTokens,
            OutputTokens = activity.OutputTokens ?? started.OutputTokens,
            CacheReadTokens = activity.CacheReadTokens ?? started.CacheReadTokens,
            CacheWriteTokens = activity.CacheWriteTokens ?? started.CacheWriteTokens,
            Cost = activity.Cost ?? started.Cost,
            Currency = Prefer(activity.Currency, started.Currency),
            TimeToFirstTokenMs = activity.TimeToFirstTokenMs ?? started.TimeToFirstTokenMs,
            ToolCalls = activity.ToolCalls ?? started.ToolCalls,
            Steps = activity.Steps ?? started.Steps,
            ReasoningEffort = Prefer(activity.ReasoningEffort, started.ReasoningEffort),
            Success = activity.Success ?? started.Success
        };
    }

    private static bool HasIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "hook", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalSession(string value)
        => value.StartsWith("external:", StringComparison.OrdinalIgnoreCase);

    private static string Prefer(string primary, string fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static CodexTaskActivity? ParseActivity(string line, string defaultProvider)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;
        var state = ReadString(root, "state", "status", "event", "type");
        if (string.IsNullOrWhiteSpace(state)) return null;
        state = NormalizeState(state);
        if (state is not ("start" or "stop")) return null;

        var provider = NormalizeProvider(ReadString(root, "provider", "source"), defaultProvider);
        var sessionId = ReadString(root, "sessionId", "session_id", "session") ?? "";
        var turnId = ReadString(root, "turnId", "turn_id", "taskId", "task_id", "id") ?? "";
        if (string.IsNullOrWhiteSpace(sessionId)) sessionId = $"external:{provider}";
        var usage = FindUsageObject(root);
        return new CodexTaskActivity(state, sessionId.Trim(), turnId.Trim())
        {
            Provider = provider,
            Model = Clean(ReadString(root, "model") ?? ReadString(usage, "model"), 160),
            InputTokens = ReadCounter(usage, "input_tokens", "inputTokens", "prompt_tokens", "promptTokens", "input_token_count", "inputTokenCount", "prompt_token_count", "promptTokenCount") ?? ReadCounter(root, "input_tokens", "inputTokens", "prompt_tokens", "promptTokens", "input_token_count", "inputTokenCount", "prompt_token_count", "promptTokenCount"),
            OutputTokens = ReadCounter(usage, "output_tokens", "outputTokens", "completion_tokens", "completionTokens", "output_token_count", "outputTokenCount", "candidates_token_count", "candidatesTokenCount", "completion_token_count", "completionTokenCount") ?? ReadCounter(root, "output_tokens", "outputTokens", "completion_tokens", "completionTokens", "output_token_count", "outputTokenCount", "candidates_token_count", "candidatesTokenCount", "completion_token_count", "completionTokenCount"),
            CacheReadTokens = ReadCounter(usage, "cache_read_tokens", "cacheReadTokens", "cached_tokens", "cachedTokens", "cache_read_input_tokens", "cacheReadInputTokens", "cache_hit_tokens", "cacheHitTokens") ?? ReadCounter(root, "cache_read_tokens", "cacheReadTokens", "cached_tokens", "cachedTokens", "cache_read_input_tokens", "cacheReadInputTokens", "cache_hit_tokens", "cacheHitTokens"),
            CacheWriteTokens = ReadCounter(usage, "cache_write_tokens", "cacheWriteTokens", "cache_creation_input_tokens", "cacheCreationInputTokens") ?? ReadCounter(root, "cache_write_tokens", "cacheWriteTokens", "cache_creation_input_tokens", "cacheCreationInputTokens"),
            Cost = ReadAmount(usage, "cost", "amount", "usage_cost", "usageCost") ?? ReadAmount(root, "cost", "amount", "usage_cost", "usageCost"),
            Currency = Clean(ReadString(usage, "currency", "cost_currency", "costCurrency") ?? ReadString(root, "currency", "cost_currency", "costCurrency"), 12).ToUpperInvariant(),
            DurationMs = ReadCounter(root, "duration_ms", "durationMs", "elapsed_ms", "elapsedMs") ?? ReadCounter(usage, "duration_ms", "durationMs"),
            TimeToFirstTokenMs = ReadCounter(usage, "time_to_first_token_ms", "timeToFirstTokenMs", "ttft_ms", "time_to_first_token", "timeToFirstToken") ?? ReadCounter(root, "time_to_first_token_ms", "timeToFirstTokenMs", "ttft_ms", "time_to_first_token", "timeToFirstToken"),
            ToolCalls = ReadCounter(root, "tool_calls", "toolCalls") ?? ReadCounter(usage, "tool_calls", "toolCalls"),
            Steps = ReadCounter(root, "steps") ?? ReadCounter(usage, "steps"),
            ReasoningEffort = Clean(ReadString(root, "reasoning_effort", "reasoningEffort", "reasoning_level", "reasoningLevel", "thinking_level", "thinkingLevel", "effort")
                ?? ReadString(usage, "reasoning_effort", "reasoningEffort", "reasoning_level", "reasoningLevel", "thinking_level", "thinkingLevel", "effort"), 32),
            Success = ReadBool(usage, "success") ?? ReadBool(root, "success")
        };
    }

    private static string NormalizeState(string state) => state.Trim().ToLowerInvariant() switch
    {
        "start" or "started" or "begin" or "began" or "working" or "running" => "start",
        "stop" or "stopped" or "end" or "ended" or "finish" or "finished" or "complete" or "completed" or "done" or "cancel" or "cancelled" or "canceled" => "stop",
        _ => state.Trim().ToLowerInvariant()
    };

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.String) return value.GetString();
            if (value.ValueKind is JsonValueKind.Number) return value.ToString();
        }
        return null;
    }

    private static long? ReadCounter(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number is >= 0 and <= 10_000_000_000 ? number : null;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return number is >= 0 and <= 10_000_000_000 ? number : null;
        }
        return null;
    }

    private static double? ReadAmount(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return double.IsFinite(number) && number is >= 0 and <= 10_000_000_000 ? number : null;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)) return double.IsFinite(number) && number is >= 0 and <= 10_000_000_000 ? number : null;
        }
        return null;
    }

    private static JsonElement FindUsageObject(JsonElement root)
    {
        // Hook payloads have changed names across client versions. Inspect a
        // small allow-list of metadata containers so a future payload that
        // wraps usage under response/result still contributes counters without
        // walking or persisting arbitrary request content.
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

    private static string Clean(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maximum ? filtered : filtered[..maximum];
    }

    private static string NormalizeProvider(string? provider, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(provider) ? fallback : provider.Trim();
        var filtered = new string(value.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(filtered)) filtered = fallback;
        return filtered.Length <= 32 ? filtered : filtered[..32];
    }

    public void Dispose() => Stop();
}
