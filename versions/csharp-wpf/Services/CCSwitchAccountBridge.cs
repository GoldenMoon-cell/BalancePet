using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Observes CC Switch's active provider rows and emits metadata-only account
/// activity. The database is opened read-only; credential values are hashed in
/// memory and never persisted, logged, or sent over the network.
/// </summary>
public sealed class CCSwitchAccountBridge : IDisposable
{
    private const int MaxRows = 16;
    private const string TargetAppType = "codex";
    private readonly string _databasePath;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _refreshCancellation;
    private Task? _refreshTask;
    private string _lastSnapshot = "";
    private bool _hasPublishedSnapshot;
    private string _lastStatusKey = "";

    public event EventHandler<AiAccountActivity>? ActivityReceived;
    public event EventHandler<CCSwitchBridgeStatus>? StatusChanged;

    public CCSwitchAccountBridge(string? databasePath = null)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cc-switch", "cc-switch.db")
            : databasePath;
    }

    public void Start()
    {
        if (_watcher is not null) return;
        var directory = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, $"{Path.GetFileName(_databasePath)}*")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnDatabaseChanged;
            _watcher.Created += OnDatabaseChanged;
            _watcher.Renamed += OnDatabaseRenamed;
            QueueRefresh();
        }
        catch (IOException) { Stop(); }
        catch (UnauthorizedAccessException) { Stop(); }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
            _refreshTask = null;
            _hasPublishedSnapshot = false;
            _lastSnapshot = "";
            _lastStatusKey = "";
        }
    }

    private void OnDatabaseChanged(object? sender, FileSystemEventArgs e) => QueueRefresh();
    private void OnDatabaseRenamed(object? sender, RenamedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        lock (_gate)
        {
            if (_watcher is null || _refreshTask is { IsCompleted: false }) return;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = new CancellationTokenSource();
            _refreshTask = RefreshAfterDelayAsync(_refreshCancellation.Token);
        }
    }

    private async Task RefreshAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            var result = ReadCurrentState();
            PublishStatus(result.Status);
            var activities = result.Activities;
            bool isInitialSnapshot;
            var snapshot = string.Join("\n", activities
                .OrderBy(activity => activity.Provider, StringComparer.Ordinal)
                .ThenBy(activity => activity.AccountLabel, StringComparer.Ordinal)
                .Select(activity => $"{activity.Provider}|{activity.AccountLabel}|{activity.Endpoint}|{activity.TokenFingerprint}|{activity.AccountType}"));
            lock (_gate)
            {
                if (string.Equals(_lastSnapshot, snapshot, StringComparison.Ordinal)) return;
                _lastSnapshot = snapshot;
                isInitialSnapshot = !_hasPublishedSnapshot;
                _hasPublishedSnapshot = true;
            }
            foreach (var activity in activities)
                ActivityReceived?.Invoke(this, activity with { IsInitialSnapshot = isInitialSnapshot });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private ReadResult ReadCurrentState()
    {
        if (!File.Exists(_databasePath))
            return new ReadResult(Array.Empty<AiAccountActivity>(), new CCSwitchBridgeStatus(CCSwitchBridgeStatusKind.DatabaseMissing, 0));
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            // CC Switch keeps one current provider per client. BalancePet is
            // tracking the Codex account that is actually used by this app;
            // Claude/Gemini current rows must not be reported as Codex login.
            command.CommandText = "SELECT app_type, name, settings_config, category, provider_type FROM providers WHERE is_current = 1 AND lower(app_type) = $appType LIMIT $limit";
            command.Parameters.AddWithValue("$appType", TargetAppType);
            command.Parameters.AddWithValue("$limit", MaxRows);
            using var reader = command.ExecuteReader();
            var activities = new List<AiAccountActivity>();
            while (reader.Read())
            {
                var appType = ReadText(reader, 0);
                var name = Clean(ReadText(reader, 1), 96);
                var config = ReadText(reader, 2);
                var category = Clean(ReadText(reader, 3), 48);
                var providerType = Clean(ReadText(reader, 4), 48);
                var details = ExtractConfigDetails(config);
                var provider = AppProviderName(appType);
                var type = ResolveAccountType(category, providerType, details.Endpoint);
                activities.Add(new AiAccountActivity(
                    "login",
                    provider,
                    type,
                    name,
                    $"CC Switch/{appType}",
                    details.Endpoint,
                    details.TokenFingerprint,
                    null,
                    ""));
            }
            var statusKind = activities.Count == 0
                ? CCSwitchBridgeStatusKind.NoCurrentCodexAccount
                : CCSwitchBridgeStatusKind.Connected;
            return new ReadResult(activities, new CCSwitchBridgeStatus(statusKind, activities.Count));
        }
        catch (SqliteException error)
        {
            var kind = error.SqliteErrorCode is 5 or 6
                ? CCSwitchBridgeStatusKind.DatabaseLocked
                : error.SqliteErrorCode == 11
                    ? CCSwitchBridgeStatusKind.DatabaseCorrupt
                    : CCSwitchBridgeStatusKind.DatabaseUnreadable;
            return new ReadResult(Array.Empty<AiAccountActivity>(), new CCSwitchBridgeStatus(kind, 0));
        }
        catch (IOException)
        {
            return new ReadResult(Array.Empty<AiAccountActivity>(), new CCSwitchBridgeStatus(CCSwitchBridgeStatusKind.DatabaseUnreadable, 0));
        }
        catch (UnauthorizedAccessException)
        {
            return new ReadResult(Array.Empty<AiAccountActivity>(), new CCSwitchBridgeStatus(CCSwitchBridgeStatusKind.DatabaseUnreadable, 0));
        }
    }

    private void PublishStatus(CCSwitchBridgeStatus status)
    {
        var key = $"{status.Kind}|{status.CurrentAccountCount}";
        lock (_gate)
        {
            if (string.Equals(_lastStatusKey, key, StringComparison.Ordinal)) return;
            _lastStatusKey = key;
        }
        StatusChanged?.Invoke(this, status);
    }

    private static string ReadText(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);

    private static string AppProviderName(string appType) => appType.Trim().ToLowerInvariant() switch
    {
        "claude" or "claude-desktop" => "Claude",
        "codex" => "Codex",
        "gemini" => "Gemini",
        "grokbuild" => "Grok",
        "opencode" => "OpenCode",
        _ => string.IsNullOrWhiteSpace(appType) ? "CC Switch" : appType.Trim()
    };

    private static string ResolveAccountType(string category, string providerType, string endpoint)
    {
        var value = $"{category} {providerType}".ToLowerInvariant();
        if (value.Contains("official")) return string.IsNullOrWhiteSpace(endpoint) ? "official" : "official-api";
        return string.IsNullOrWhiteSpace(endpoint) || value.Contains("custom") || value.Contains("relay") || value.Contains("third")
            ? "relay-api"
            : "third-party";
    }

    private static ConfigDetails ExtractConfigDetails(string? settingsConfig)
    {
        if (string.IsNullOrWhiteSpace(settingsConfig)) return new ConfigDetails("", "");
        try
        {
            using var document = JsonDocument.Parse(settingsConfig);
            var endpoint = "";
            var fingerprint = "";
            WalkJson(document.RootElement, ref endpoint, ref fingerprint);
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                var configText = document.RootElement.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.String
                    ? config.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(configText))
                    ExtractTextConfig(configText, ref endpoint, ref fingerprint);
            }
            return new ConfigDetails(NormalizeEndpoint(endpoint), fingerprint);
        }
        catch (JsonException) { return new ConfigDetails("", ""); }
    }

    private static void WalkJson(JsonElement value, ref string endpoint, ref string fingerprint, string property = "")
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in value.EnumerateObject()) WalkJson(child.Value, ref endpoint, ref fingerprint, child.Name);
            return;
        }
        if (value.ValueKind != JsonValueKind.String) return;
        var text = value.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(endpoint) && IsEndpointName(property)) endpoint = text;
        if (string.IsNullOrWhiteSpace(fingerprint) && IsCredentialName(property)) fingerprint = Fingerprint(text);
    }

    private static void ExtractTextConfig(string text, ref string endpoint, ref string fingerprint)
    {
        foreach (Match match in Regex.Matches(text, "(?i)(?<name>base[_-]?url|endpoint|api[_-]?url|api[_-]?key|auth[_-]?token|token)\\s*[:=]\\s*[\\\"'](?<value>[^\\\"']+)", RegexOptions.CultureInvariant))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(endpoint) && IsEndpointName(name)) endpoint = value;
            if (string.IsNullOrWhiteSpace(fingerprint) && IsCredentialName(name)) fingerprint = Fingerprint(value);
        }
    }

    private static bool IsEndpointName(string name)
    {
        var normalized = name.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "baseurl" or "endpoint" or "apiurl" or "anthropicbaseurl" or "openaibaseurl";
    }

    private static bool IsCredentialName(string name)
    {
        var normalized = name.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("apikey", StringComparison.Ordinal) || normalized.Contains("authtoken", StringComparison.Ordinal)
            || normalized is "token" or "key";
    }

    private static string Fingerprint(string value)
    {
        var text = value.Trim();
        if (text.Length < 12 || text.Length > 4096 || text.Contains("<", StringComparison.Ordinal)) return "";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string NormalizeEndpoint(string value)
    {
        var text = Clean(value, 160);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }

    public void Dispose() => Stop();

    private sealed record ConfigDetails(string Endpoint, string TokenFingerprint);
    private sealed record ReadResult(IReadOnlyList<AiAccountActivity> Activities, CCSwitchBridgeStatus Status);
}
