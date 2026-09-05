using System.IO;
using System.IO.Pipes;
using System.Globalization;
using System.Text.Json;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Metadata-only login status reported by an AI client or CLI.
/// Tokens are never sent through this protocol; TokenFingerprint is an
/// optional SHA-256 value used only to match a local monitor profile.
/// </summary>
public sealed record AiAccountActivity(
    string State,
    string Provider,
    string AccountType,
    string AccountLabel,
    string Source,
    string Endpoint,
    string TokenFingerprint,
    double? ReportedBalance,
    string Currency)
{
    public bool IsLogin => State == "login";
}

public sealed class AccountStatusBridge : IDisposable
{
    public const string PipeName = "BalancePet.Account.v1";
    private const int MaxMessageLength = 8192;

    private CancellationTokenSource? _cancellation;
    private Task? _listener;

    public event EventHandler<AiAccountActivity>? ActivityReceived;

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
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await ProcessClientAsync(pipe, cancellationToken);
                pipe = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await Task.Delay(250, cancellationToken);
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private async Task ProcessClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line) || line.Length > MaxMessageLength) return;
                var activity = Parse(line);
                if (activity is not null) ActivityReceived?.Invoke(this, activity);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    private static AiAccountActivity? Parse(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;
        var state = NormalizeState(ReadString(root, "state", "status", "event"));
        if (state is not ("login" or "logout")) return null;

        var provider = Clean(ReadString(root, "provider", "source_provider"), 32);
        if (string.IsNullOrWhiteSpace(provider)) return null;
        var accountType = NormalizeAccountType(ReadString(root, "accountType", "account_type", "kind", "type"));
        var label = Clean(ReadString(root, "accountLabel", "account_label", "account", "email"), 96);
        var source = Clean(ReadString(root, "source", "api_source", "origin"), 160);
        var endpoint = NormalizeEndpoint(ReadString(root, "endpoint", "api_endpoint", "url"));
        var fingerprint = NormalizeFingerprint(ReadString(root, "tokenFingerprint", "token_fingerprint", "keyFingerprint", "key_fingerprint"));
        var reportedBalance = ReadNumber(root, "balance", "remaining", "available_balance");
        var currency = Clean(ReadString(root, "currency", "unit"), 16).ToUpperInvariant();
        return new AiAccountActivity(state, provider, accountType, label, source, endpoint, fingerprint, reportedBalance, currency);
    }

    private static string NormalizeState(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "login" or "logged-in" or "logged_in" or "connected" or "start" => "login",
        "logout" or "logged-out" or "logged_out" or "disconnected" or "stop" => "logout",
        _ => ""
    };

    private static string NormalizeAccountType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "official" or "official-account" or "official_account" or "web" => "official",
        "official-api" or "official_api" or "api" => "official-api",
        "relay" or "relay-api" or "relay_api" or "proxy" or "中转站" => "relay-api",
        "third-party" or "third_party" or "thirdparty" => "third-party",
        _ => "unknown"
    };

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }

    private static string NormalizeFingerprint(string? value)
    {
        var text = value?.Trim() ?? "";
        return text.Length == 64 && text.All(Uri.IsHexDigit) ? text.ToLowerInvariant() : "";
    }

    private static string NormalizeEndpoint(string? value)
    {
        var text = Clean(value, 160);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    private static double? ReadNumber(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) return number;
            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && double.IsFinite(number)) return number;
        }
        return null;
    }

    public void Dispose() => Stop();
}
