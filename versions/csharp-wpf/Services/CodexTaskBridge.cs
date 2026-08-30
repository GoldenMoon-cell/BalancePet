using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace BalancePet.Wpf.Services;

public sealed record CodexTaskActivity(string State, string SessionId, string TurnId)
{
    public string Key => $"{SessionId}:{TurnId}";
}

public sealed class CodexTaskBridge : IDisposable
{
    public const string PipeName = "BalancePet.CodexTask.v1";

    private CancellationTokenSource? _cancellation;
    private Task? _listener;

    public event EventHandler<CodexTaskActivity>? ActivityReceived;

    public void Start()
    {
        if (_listener is { IsCompleted: false }) return;
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
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                var activity = JsonSerializer.Deserialize<CodexTaskActivity>(line, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (activity is null || activity.State is not ("start" or "stop")) continue;
                if (string.IsNullOrWhiteSpace(activity.SessionId) || string.IsNullOrWhiteSpace(activity.TurnId)) continue;
                ActivityReceived?.Invoke(this, activity);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Delay(250, cancellationToken);
            }
            catch (JsonException)
            {
                // Ignore malformed local messages without interrupting the listener.
            }
        }
    }

    public void Dispose() => Stop();
}
