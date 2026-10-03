using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.NotificationCenter;

public sealed class NotificationLiveState
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
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

    /// <summary>
    /// What the host's own windows look like, or null when it did not say.
    /// </summary>
    /// <remarks>
    /// Null means "keep your own colours": an older host does not publish this, and neither
    /// does one that could not resolve a theme. Guessing would be worse than not following.
    /// </remarks>
    [JsonPropertyName("appearance")] public NotificationAppearance? Appearance { get; set; }

    public bool HasBalance => Balance.HasValue && double.IsFinite(Balance.Value);
    public bool HasSpent => Spent.HasValue && double.IsFinite(Spent.Value);

    public static NotificationLiveState? Read(string directory)
    {
        var root = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet")
            : directory;
        var path = Path.Combine(root, "notification-state.v1.json");
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<NotificationLiveState>(File.ReadAllText(path), JsonOptions);
            return state?.Schema == "balancepet.notification-state.v1" ? state : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

/// <summary>
/// The host's colours and face, as the host reports them.
/// </summary>
/// <remarks>
/// Resolved values rather than a theme name: this extension has no business finding and
/// parsing another program's theme package, and even if it did, nothing in that package says
/// which one is active. Every colour is optional — one the host did not send leaves this
/// extension's own colour in place rather than becoming black.
/// </remarks>
public sealed class NotificationAppearance
{
    [JsonPropertyName("theme_mode")] public string ThemeMode { get; set; } = "light";
    [JsonPropertyName("font")] public string Font { get; set; } = "";
    [JsonPropertyName("window")] public string Window { get; set; } = "";
    [JsonPropertyName("sidebar")] public string Sidebar { get; set; } = "";
    [JsonPropertyName("surface")] public string Surface { get; set; } = "";
    [JsonPropertyName("control")] public string Control { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("muted")] public string Muted { get; set; } = "";
    [JsonPropertyName("border")] public string Border { get; set; } = "";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "";
    [JsonPropertyName("accent_soft")] public string AccentSoft { get; set; } = "";

    public bool IsDark => string.Equals(ThemeMode, "dark", StringComparison.OrdinalIgnoreCase);

    /// <summary>A fingerprint, so the palette is only reapplied when it actually changed.</summary>
    public string Fingerprint =>
        $"{ThemeMode}|{Font}|{Window}|{Sidebar}|{Surface}|{Control}|{Text}|{Muted}|{Border}|{Accent}|{AccentSoft}";
}
