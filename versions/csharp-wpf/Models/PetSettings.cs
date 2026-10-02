using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Models;

public sealed class PetSettings
{
    [JsonPropertyName("settings_schema_version")] public int SettingsSchemaVersion { get; set; }
    [JsonPropertyName("language")] public string Language { get; set; } = "zh-CN";
    [JsonPropertyName("theme_id")] public string ThemeId { get; set; } = "balancepet.theme.mica";
    [JsonPropertyName("theme_mode")] public string ThemeMode { get; set; } = "system";
    [JsonPropertyName("theme_backdrop")] public string ThemeBackdrop { get; set; } = "mica";
    // Empty means the face embedded in the assembly. A name rather than a flag, because
    // the interesting states are "the bundled one" and "this particular installed one",
    // and a font that is later uninstalled has to fall back rather than fail.
    [JsonPropertyName("ui_font")] public string UiFont { get; set; } = "";
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "https://ai.websee.top/api/v1/auth/me?timezone=Asia%2FShanghai";
    [JsonPropertyName("auth_mode")] public string AuthMode { get; set; } = "authorization";
    [JsonPropertyName("header_name")] public string HeaderName { get; set; } = "Authorization";
    [JsonPropertyName("token_blob")] public string TokenBlob { get; set; } = "";
    [JsonPropertyName("balance_path")] public string BalancePath { get; set; } = "balance";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
    [JsonPropertyName("refresh_seconds")] public int RefreshSeconds { get; set; } = 60;
    [JsonPropertyName("auto_refresh_enabled")] public bool AutoRefreshEnabled { get; set; } = true;
    [JsonPropertyName("low_threshold")] public double LowThreshold { get; set; } = 5;
    // Deliberately still DeepSeek, and not the placeholder. A settings file written
    // before appearances became packages has no pet_style key, so this default is what
    // those installations load -- and the appearance it names is the one they have been
    // looking at. Their folder is converted into a package on first launch, so it still
    // resolves. Changing this to the placeholder would silently swap the pet on every
    // upgrade that never touched the setting. A fresh installation has nothing named
    // deepseek, and there the name resolution falls back on its own.
    [JsonPropertyName("pet_style")] public string PetStyle { get; set; } = "deepseek";
    [JsonPropertyName("interaction_mode")] public string InteractionMode { get; set; } = "free";
    [JsonPropertyName("pet_scale")] public double Scale { get; set; } = 1;
    [JsonPropertyName("window_x")] public int WindowX { get; set; } = -1;
    [JsonPropertyName("window_y")] public int WindowY { get; set; } = -1;
    [JsonPropertyName("flipped")] public bool Flipped { get; set; }
    [JsonPropertyName("sound")] public bool Sound { get; set; } = true;
    [JsonPropertyName("volume")] public double Volume { get; set; } = 0.35;
    [JsonPropertyName("bubble")] public bool Bubble { get; set; } = true;
    [JsonPropertyName("interaction_effects")] public bool InteractionEffects { get; set; } = true;
    [JsonPropertyName("navigation_animations")] public bool NavigationAnimations { get; set; } = true;
    [JsonPropertyName("settings_navigation_collapsed")] public bool NavigationCollapsed { get; set; }
    [JsonPropertyName("random_easter_eggs")] public bool RandomEasterEggs { get; set; } = true;
    [JsonPropertyName("codex_task_integration")] public bool CodexTaskIntegration { get; set; }
    // DeepSeek Harness reports through the same provider-neutral pipe as other
    // third-party clients, so it gets its own opt-in instead of riding on the
    // Codex switch.
    [JsonPropertyName("deepseek_harness_integration")] public bool DeepSeekHarnessIntegration { get; set; }
    // Each hook-based client is gated separately. Before schema 4 a single
    // codex_task_integration switch covered all of them; Migrate() expands the
    // legacy value so an upgrade never silently stops following a client.
    [JsonPropertyName("gemini_task_integration")] public bool GeminiTaskIntegration { get; set; }
    [JsonPropertyName("qwen_task_integration")] public bool QwenTaskIntegration { get; set; }
    [JsonPropertyName("claude_task_integration")] public bool ClaudeTaskIntegration { get; set; }
    // Fallback bucket for any provider that matches no known client, so custom
    // CLIs calling balancepet-task.ps1 keep working.
    [JsonPropertyName("other_task_integration")] public bool OtherTaskIntegration { get; set; }
    // Keep the JSON name so existing settings continue to work after the
    // login-state source was narrowed to CC Switch.
    [JsonPropertyName("update_check_mode")] public string UpdateCheckMode { get; set; } = "daily";
    [JsonPropertyName("last_update_check_utc")] public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    [JsonPropertyName("extension_update_check_mode")] public string ExtensionUpdateCheckMode { get; set; } = "daily";
    [JsonPropertyName("last_extension_update_check_utc")] public DateTimeOffset? LastExtensionUpdateCheckUtc { get; set; }
    // The changelog notices the user has already been told about, held as the highest
    // sequence number seen rather than as a date or a list of ids. A sequence survives
    // the feed dropping an old entry, and two notices published on the same day cannot
    // hide each other behind a comparison that has only day resolution.
    [JsonPropertyName("notices_seen_seq")] public int NoticesSeenSeq { get; set; }
    // On by default, and it lives in the changelog window rather than among the
    // interaction switches, because that is where someone who is annoyed by it will
    // look. A notification nobody can find the switch for is worse than none.
    [JsonPropertyName("notices_notify")] public bool NoticesNotify { get; set; } = true;
    [JsonPropertyName("start_with_windows")] public bool StartWithWindows { get; set; }
    [JsonPropertyName("monitors")] public List<MonitorProfile> Monitors { get; set; } = new();
    [JsonPropertyName("selected_monitor_id")] public string SelectedMonitorId { get; set; } = "";
}
