using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace BalancePet.Wpf.Services;

public sealed record CodexTaskActivity(string State, string SessionId, string TurnId)
{
    public string Key => $"{SessionId}:{TurnId}";
    public string Provider { get; init; } = "Codex";
}

public sealed class CodexTaskBridge : IDisposable
{
    // Keep the original pipe for existing Codex hooks. Other clients should
    // use the provider-neutral pipe below or the bundled sender script.
    public const string PipeName = "BalancePet.CodexTask.v1";
    public const string GenericPipeName = "BalancePet.Task.v1";

    private CancellationTokenSource? _cancellation;
    private Task[] _listeners = Array.Empty<Task>();

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
                ActivityReceived?.Invoke(this, activity);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

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
        return new CodexTaskActivity(state, sessionId.Trim(), turnId.Trim()) { Provider = provider };
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

    private static string NormalizeProvider(string? provider, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(provider) ? fallback : provider.Trim();
        var filtered = new string(value.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(filtered)) filtered = fallback;
        return filtered.Length <= 32 ? filtered : filtered[..32];
    }

    public void Dispose() => Stop();
}
