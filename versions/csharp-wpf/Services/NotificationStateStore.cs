using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Publishes the current, credential-free state used by the notification
/// center's around-pet presentation. It is separate from the historical event
/// stream so a stale event cannot masquerade as the current state.
/// </summary>
public sealed class NotificationStateStore
{
    public const string Schema = "balancepet.notification-state.v1";
    public const string FileName = "notification-state.v1.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly object _gate = new();
    private readonly string _path;

    public NotificationStateStore(string? directory = null)
    {
        var root = string.IsNullOrWhiteSpace(directory)
            ? UsageEventBridge.GetDefaultDirectory()
            : directory;
        _path = Path.Combine(root, FileName);
    }

    public static string GetDefaultPath() => Path.Combine(UsageEventBridge.GetDefaultDirectory(), FileName);

    /// <summary>
    /// What the host's own windows look like, so an extension's windows can match them.
    /// </summary>
    /// <remarks>
    /// The resolved colours, not the name of a theme package: an extension that had to find
    /// and parse someone else's theme file would break the first time that format changed,
    /// and would still not know which of several installed themes is the active one. The
    /// host knows both, so it says what it is actually using.
    /// </remarks>
    public sealed record AppearanceSnapshot(
        string ThemeMode,
        string Font,
        string Window,
        string Surface,
        string Control,
        string Text,
        string Muted,
        string Border,
        string Accent,
        string AccentSoft);

    public void Publish(
        string coreVersion,
        bool taskKnown,
        bool taskActive,
        string taskProvider,
        int taskCount,
        bool loginKnown,
        string loginMode,
        string loginDetail,
        double? balance,
        string currency,
        double? spent,
        string spentCurrency,
        AppearanceSnapshot? appearance = null)
    {
        var document = new NotificationStateDocument
        {
            Schema = Schema,
            UpdatedAt = DateTimeOffset.Now,
            CoreVersion = Clean(coreVersion, 64),
            TaskKnown = taskKnown,
            TaskActive = taskActive,
            TaskProvider = Clean(taskProvider, 64),
            TaskCount = Math.Clamp(taskCount, 0, 1000),
            LoginKnown = loginKnown,
            LoginMode = Clean(loginMode, 48),
            LoginDetail = Clean(loginDetail, 160),
            Balance = ClampAmount(balance),
            Currency = CleanCurrency(currency),
            Spent = ClampAmount(spent),
            SpentCurrency = CleanCurrency(spentCurrency),
            Appearance = appearance is null ? null : new NotificationAppearanceDocument
            {
                ThemeMode = appearance.ThemeMode == "dark" ? "dark" : "light",
                Font = Clean(appearance.Font, 96),
                Window = Clean(appearance.Window, 16),
                Surface = Clean(appearance.Surface, 16),
                Control = Clean(appearance.Control, 16),
                Text = Clean(appearance.Text, 16),
                Muted = Clean(appearance.Muted, 16),
                Border = Clean(appearance.Border, 16),
                Accent = Clean(appearance.Accent, 16),
                AccentSoft = Clean(appearance.AccentSoft, 16)
            }
        };

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options), new UTF8Encoding(false));
                File.Move(temporary, _path, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Absent when the host could not resolve a theme, which an extension treats
    /// as "keep using your own colours".</summary>
    private sealed class NotificationAppearanceDocument
    {
        [JsonPropertyName("theme_mode")] public string ThemeMode { get; set; } = "light";
        [JsonPropertyName("font")] public string Font { get; set; } = "";
        [JsonPropertyName("window")] public string Window { get; set; } = "";
        [JsonPropertyName("surface")] public string Surface { get; set; } = "";
        [JsonPropertyName("control")] public string Control { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("muted")] public string Muted { get; set; } = "";
        [JsonPropertyName("border")] public string Border { get; set; } = "";
        [JsonPropertyName("accent")] public string Accent { get; set; } = "";
        [JsonPropertyName("accent_soft")] public string AccentSoft { get; set; } = "";
    }

    private sealed class NotificationStateDocument
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = NotificationStateStore.Schema;
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("appearance")] public NotificationAppearanceDocument? Appearance { get; set; }
        [JsonPropertyName("core_version")] public string CoreVersion { get; set; } = "";
        [JsonPropertyName("task_known")] public bool TaskKnown { get; set; }
        [JsonPropertyName("task_active")] public bool TaskActive { get; set; }
        [JsonPropertyName("task_provider")] public string TaskProvider { get; set; } = "";
        [JsonPropertyName("task_count")] public int TaskCount { get; set; }
        [JsonPropertyName("login_known")] public bool LoginKnown { get; set; }
        [JsonPropertyName("login_mode")] public string LoginMode { get; set; } = "";
        [JsonPropertyName("login_detail")] public string LoginDetail { get; set; } = "";
        [JsonPropertyName("balance")] public double? Balance { get; set; }
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("spent")] public double? Spent { get; set; }
        [JsonPropertyName("spent_currency")] public string SpentCurrency { get; set; } = "USD";
    }

    private static double? ClampAmount(double? value)
        => value.HasValue && double.IsFinite(value.Value) && value.Value is >= -10_000_000_000 and <= 10_000_000_000
            ? value
            : null;

    private static string CleanCurrency(string? value)
    {
        var currency = Clean(value, 12).ToUpperInvariant();
        return currency.Length == 0 || !currency.All(character => character is >= 'A' and <= 'Z' or ' ')
            ? "USD"
            : currency;
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }
}
