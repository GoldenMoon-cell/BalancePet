using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Reads only the allow-listed token_usage_record counters written by Codex.
/// The rollout files contain conversation data, so this reader never stores,
/// logs, or forwards complete lines or arbitrary JSON values.
/// </summary>
public static class CodexUsageReader
{
    private const int MaxLineLength = 1_048_576;
    // A resumed Codex thread appends to its newest rollout file. Keeping a
    // small newest-first window avoids rescanning the entire conversation
    // history after every completed task.
    private const int MaxFiles = 2;
    private const int Attempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(180);

    public static async Task<CodexUsageCounters?> TryReadTurnAsync(
        string sessionId,
        string turnId,
        DateTimeOffset? startedAt,
        CancellationToken cancellationToken = default)
    {
        return (await TryReadTurnSnapshotAsync(sessionId, turnId, startedAt, cancellationToken))?.Counters;
    }

    public static async Task<CodexUsageSnapshot?> TryReadTurnSnapshotAsync(
        string sessionId,
        string turnId,
        DateTimeOffset? startedAt,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeId(sessionId)) return null;

        CodexUsageSnapshot? result = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            result = await ReadTurnAsync(sessionId, turnId, startedAt, cancellationToken);
            if (result?.Counters?.HasTokenData == true || result?.Requests.Count > 0 || attempt == Attempts - 1)
                return result;
            await Task.Delay(RetryDelay, cancellationToken);
        }

        return result;
    }

    private static async Task<CodexUsageSnapshot?> ReadTurnAsync(
        string sessionId,
        string turnId,
        DateTimeOffset? startedAt,
        CancellationToken cancellationToken)
    {
        var files = FindRolloutFiles(sessionId).Take(MaxFiles).ToArray();
        if (files.Length == 0) return null;

        var accumulator = new UsageAccumulator(turnId, startedAt);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReadFileAsync(file, sessionId, accumulator, cancellationToken);
        }

        return accumulator.ToSnapshot();
    }

    private static IEnumerable<string> FindRolloutFiles(string sessionId)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(profile, ".codex", "sessions"),
            Path.Combine(profile, ".codex", "archived_sessions")
        };

        foreach (var root in roots)
        {
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path).Contains(sessionId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files) yield return file;
        }
    }

    private static async Task ReadFileAsync(
        string path,
        string sessionId,
        UsageAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 32 * 1024,
                options: FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.Length > MaxLineLength) continue;
                if (line.Contains("\"token_usage_record\"", StringComparison.Ordinal))
                    TryReadRecord(line, sessionId, accumulator);
                else if (line.Contains("\"turn_context\"", StringComparison.Ordinal))
                    TryReadTurnContext(line, sessionId, accumulator);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    private static void TryReadRecord(string line, string sessionId, UsageAccumulator accumulator)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type) || type != "token_usage_record") return;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return;

            var recordSession = ReadString(payload, "session_id") ?? ReadString(payload, "thread_id");
            if (!string.IsNullOrWhiteSpace(recordSession) && !string.Equals(recordSession, sessionId, StringComparison.Ordinal)) return;
            var recordTurn = ReadString(payload, "turn_id") ?? ReadString(payload, "root_turn_id") ?? "";
            var occurredAt = ReadDate(root, "timestamp");
            if (!accumulator.Matches(recordTurn, occurredAt)) return;

            var responseId = ReadString(payload, "response_id") ?? "";
            var cumulative = FindObject(payload, "turn_token_usage");
            if (cumulative.HasValue && HasCounter(cumulative.Value))
            {
                accumulator.AddCumulative(occurredAt, cumulative.Value);
                accumulator.AddReasoning(occurredAt, ReadString(payload, "reasoning_effort", "reasoning_level", "thinking_level", "effort")
                    ?? ReadString(cumulative.Value, "reasoning_effort", "reasoning_level", "thinking_level", "effort"));
            }

            var usage = FindObject(payload, "usage");
            if (usage.HasValue && HasCounter(usage.Value))
            {
                accumulator.AddIncremental(occurredAt, responseId, usage.Value);
                accumulator.AddReasoning(occurredAt, ReadString(payload, "reasoning_effort", "reasoning_level", "thinking_level", "effort")
                    ?? ReadString(usage.Value, "reasoning_effort", "reasoning_level", "thinking_level", "effort"));
            }
        }
        catch (JsonException) { }
    }

    private static void TryReadTurnContext(string line, string sessionId, UsageAccumulator accumulator)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type) || type != "turn_context") return;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return;
            // turn_context records may omit the thread/session ID and only
            // carry root_turn_id (which is a turn ID, not the thread ID).
            var recordSession = ReadString(payload, "thread_id") ?? ReadString(payload, "session_id");
            if (!string.IsNullOrWhiteSpace(recordSession) && !string.Equals(recordSession, sessionId, StringComparison.Ordinal)) return;
            var recordTurn = ReadString(payload, "turn_id") ?? "";
            var occurredAt = ReadDate(root, "timestamp");
            if (!accumulator.Matches(recordTurn, occurredAt)) return;
            var model = ReadString(payload, "model");
            if (!string.IsNullOrWhiteSpace(model)) accumulator.AddModel(occurredAt, model);
            accumulator.AddReasoning(occurredAt, ReadString(payload, "reasoning_effort", "reasoning_level", "thinking_level", "effort"));
        }
        catch (JsonException) { }
    }

    private static JsonElement? FindObject(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static bool HasCounter(JsonElement value)
        => ReadCounter(value, "input_tokens", "output_tokens", "cached_input_tokens", "cache_read_tokens", "cache_write_input_tokens", "cache_write_tokens").HasValue
            || ReadCounter(value, "input_tokens_details.cached_tokens").HasValue;

    private static bool IsSafeId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = ReadString(root, name) ?? "";
        return value.Length > 0;
    }

    private static DateTimeOffset? ReadDate(JsonElement root, string name)
        => ReadString(root, name) is { } value
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static long? ReadCounter(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var current = root;
            foreach (var segment in name.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    current = default;
                    break;
                }
            }

            if (current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var number) && number is >= 0 and <= 10_000_000_000)
                return number;
            if (current.ValueKind == JsonValueKind.String && long.TryParse(current.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number is >= 0 and <= 10_000_000_000)
                return number;
        }

        return null;
    }

    private sealed class UsageAccumulator
    {
        private readonly string _turnId;
        private readonly DateTimeOffset? _startedAt;
        private readonly HashSet<string> _responses = new(StringComparer.Ordinal);
        private readonly List<CodexUsageRequest> _requests = new();
        private DateTimeOffset _latestCumulativeAt = DateTimeOffset.MinValue;
        private DateTimeOffset _latestModelAt = DateTimeOffset.MinValue;
        private DateTimeOffset _latestReasoningAt = DateTimeOffset.MinValue;
        private JsonElement? _latestCumulative;
        private string _model = "";
        private string _reasoningEffort = "";
        private long _input;
        private long _output;
        private long _cacheRead;
        private long _cacheWrite;
        private bool _hasInput;
        private bool _hasOutput;
        private bool _hasCacheRead;
        private bool _hasCacheWrite;

        public UsageAccumulator(string turnId, DateTimeOffset? startedAt)
        {
            _turnId = turnId ?? "";
            _startedAt = startedAt;
        }

        public bool Matches(string recordTurn, DateTimeOffset? occurredAt)
        {
            if (!string.IsNullOrWhiteSpace(_turnId)) return string.Equals(recordTurn, _turnId, StringComparison.Ordinal);
            return !_startedAt.HasValue || !occurredAt.HasValue || occurredAt.Value >= _startedAt.Value.AddSeconds(-5);
        }

        public void AddCumulative(DateTimeOffset? occurredAt, JsonElement value)
        {
            if (occurredAt.HasValue && occurredAt.Value < _latestCumulativeAt) return;
            _latestCumulativeAt = occurredAt ?? _latestCumulativeAt;
            // JsonElement points into the JsonDocument for the current line;
            // clone it before that document is disposed.
            _latestCumulative = value.Clone();
        }

        public void AddModel(DateTimeOffset? occurredAt, string model)
        {
            if (occurredAt.HasValue && occurredAt.Value < _latestModelAt) return;
            _latestModelAt = occurredAt ?? _latestModelAt;
            _model = model.Length <= 160 ? model : model[..160];
        }

        public void AddReasoning(DateTimeOffset? occurredAt, string? reasoningEffort)
        {
            if (string.IsNullOrWhiteSpace(reasoningEffort)) return;
            if (occurredAt.HasValue && occurredAt.Value < _latestReasoningAt) return;
            _latestReasoningAt = occurredAt ?? _latestReasoningAt;
            var cleaned = reasoningEffort.Trim();
            _reasoningEffort = cleaned.Length <= 32 ? cleaned : cleaned[..32];
        }

        public void AddIncremental(DateTimeOffset? occurredAt, string responseId, JsonElement value)
        {
            var dedupeKey = !string.IsNullOrWhiteSpace(responseId)
                ? responseId
                : string.Join('|', occurredAt?.UtcTicks, ReadCounter(value, "input_tokens"), ReadCounter(value, "output_tokens"), _requests.Count);
            if (!_responses.Add(dedupeKey)) return;
            var input = ReadCounter(value, "input_tokens");
            var output = ReadCounter(value, "output_tokens");
            var cacheRead = ReadCounter(value, "cached_input_tokens", "cache_read_tokens") ?? ReadCounter(value, "input_tokens_details.cached_tokens");
            var cacheWrite = ReadCounter(value, "cache_write_input_tokens", "cache_write_tokens");
            Add(ref _input, ref _hasInput, input);
            Add(ref _output, ref _hasOutput, output);
            Add(ref _cacheRead, ref _hasCacheRead, cacheRead);
            Add(ref _cacheWrite, ref _hasCacheWrite, cacheWrite);
            _requests.Add(new CodexUsageRequest(
                string.IsNullOrWhiteSpace(_turnId) ? "" : _turnId,
                responseId,
                occurredAt ?? _startedAt ?? DateTimeOffset.Now,
                _model,
                _reasoningEffort,
                input,
                output,
                cacheRead,
                cacheWrite));
        }

        public CodexUsageSnapshot ToSnapshot()
        {
            CodexUsageCounters? counters;
            if (_latestCumulative.HasValue)
            {
                var value = _latestCumulative.Value;
                counters = new CodexUsageCounters(
                    ReadCounter(value, "input_tokens"),
                    ReadCounter(value, "output_tokens"),
                    ReadCounter(value, "cached_input_tokens", "cache_read_tokens") ?? ReadCounter(value, "input_tokens_details.cached_tokens"),
                    ReadCounter(value, "cache_write_input_tokens", "cache_write_tokens"),
                    _model,
                    _reasoningEffort);
            }

            else
            {
                counters = _hasInput || _hasOutput || _hasCacheRead || _hasCacheWrite
                ? new CodexUsageCounters(
                    _hasInput ? _input : null,
                    _hasOutput ? _output : null,
                    _hasCacheRead ? _cacheRead : null,
                    _hasCacheWrite ? _cacheWrite : null,
                    _model,
                    _reasoningEffort)
                : null;
            }

            var requests = _requests.Count > 0
                ? _requests.Select(request => request with
                {
                    Model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model,
                    ReasoningEffort = string.IsNullOrWhiteSpace(request.ReasoningEffort) ? _reasoningEffort : request.ReasoningEffort
                }).ToArray()
                : counters?.HasTokenData == true
                    ? new[] { new CodexUsageRequest(
                        string.IsNullOrWhiteSpace(_turnId) ? "" : _turnId,
                        "",
                        _latestCumulativeAt != DateTimeOffset.MinValue ? _latestCumulativeAt : _startedAt ?? DateTimeOffset.Now,
                        _model,
                        _reasoningEffort,
                        counters.InputTokens,
                        counters.OutputTokens,
                        counters.CacheReadTokens,
                        counters.CacheWriteTokens) }
                    : Array.Empty<CodexUsageRequest>();
            return new CodexUsageSnapshot(counters, requests);
        }

        private static void Add(ref long total, ref bool hasValue, long? value)
        {
            if (!value.HasValue) return;
            total = Math.Clamp(total + value.Value, 0, 10_000_000_000);
            hasValue = true;
        }
    }
}

public sealed record CodexUsageCounters(
    long? InputTokens,
    long? OutputTokens,
    long? CacheReadTokens,
    long? CacheWriteTokens,
    string Model,
    string ReasoningEffort = "")
{
    public bool HasData => InputTokens.HasValue || OutputTokens.HasValue || CacheReadTokens.HasValue || CacheWriteTokens.HasValue;
    public bool HasTokenData => HasData;
    public bool HasModel => !string.IsNullOrWhiteSpace(Model);
}

public sealed record CodexUsageSnapshot(
    CodexUsageCounters? Counters,
    IReadOnlyList<CodexUsageRequest> Requests);

public sealed record CodexUsageRequest(
    string TurnId,
    string ResponseId,
    DateTimeOffset OccurredAt,
    string Model,
    string ReasoningEffort,
    long? InputTokens,
    long? OutputTokens,
    long? CacheReadTokens,
    long? CacheWriteTokens);
