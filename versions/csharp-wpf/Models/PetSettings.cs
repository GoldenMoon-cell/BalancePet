using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Models;

public sealed class PetSettings
{
    [JsonPropertyName("settings_schema_version")] public int SettingsSchemaVersion { get; set; }
    [JsonPropertyName("language")] public string Language { get; set; } = "zh-CN";
    [JsonPropertyName("theme_id")] public string ThemeId { get; set; } = "balancepet.theme.mica";
    [JsonPropertyName("theme_mode")] public string ThemeMode { get; set; } = "system";
    [JsonPropertyName("theme_backdrop")] public string ThemeBackdrop { get; set; } = "mica";
    [JsonPropertyName("theme_compact")] public bool ThemeCompact { get; set; }
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "https://ai.websee.top/api/v1/auth/me?timezone=Asia%2FShanghai";
    [JsonPropertyName("auth_mode")] public string AuthMode { get; set; } = "authorization";
    [JsonPropertyName("header_name")] public string HeaderName { get; set; } = "Authorization";
    [JsonPropertyName("token_blob")] public string TokenBlob { get; set; } = "";
    [JsonPropertyName("balance_path")] public string BalancePath { get; set; } = "balance";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
    [JsonPropertyName("refresh_seconds")] public int RefreshSeconds { get; set; } = 60;
    [JsonPropertyName("auto_refresh_enabled")] public bool AutoRefreshEnabled { get; set; } = true;
    [JsonPropertyName("low_threshold")] public double LowThreshold { get; set; } = 5;
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
    [JsonPropertyName("account_status_integration")] public bool CCSwitchIntegration { get; set; } = true;
    [JsonPropertyName("update_check_mode")] public string UpdateCheckMode { get; set; } = "daily";
    [JsonPropertyName("last_update_check_utc")] public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    [JsonPropertyName("extension_update_check_mode")] public string ExtensionUpdateCheckMode { get; set; } = "daily";
    [JsonPropertyName("last_extension_update_check_utc")] public DateTimeOffset? LastExtensionUpdateCheckUtc { get; set; }
    [JsonPropertyName("start_with_windows")] public bool StartWithWindows { get; set; }
    [JsonPropertyName("monitors")] public List<MonitorProfile> Monitors { get; set; } = new();
    [JsonPropertyName("selected_monitor_id")] public string SelectedMonitorId { get; set; } = "";
}
