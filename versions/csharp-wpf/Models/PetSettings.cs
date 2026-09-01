using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Models;

public sealed class PetSettings
{
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "https://ai.websee.top/api/v1/auth/me?timezone=Asia%2FShanghai";
    [JsonPropertyName("auth_mode")] public string AuthMode { get; set; } = "authorization";
    [JsonPropertyName("header_name")] public string HeaderName { get; set; } = "Authorization";
    [JsonPropertyName("token_blob")] public string TokenBlob { get; set; } = "";
    [JsonPropertyName("balance_path")] public string BalancePath { get; set; } = "balance";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
    [JsonPropertyName("refresh_seconds")] public int RefreshSeconds { get; set; } = 60;
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
    [JsonPropertyName("random_easter_eggs")] public bool RandomEasterEggs { get; set; } = true;
    [JsonPropertyName("codex_task_integration")] public bool CodexTaskIntegration { get; set; }
    [JsonPropertyName("system_notifications")] public bool SystemNotifications { get; set; } = true;
    [JsonPropertyName("update_check_mode")] public string UpdateCheckMode { get; set; } = "daily";
    [JsonPropertyName("last_update_check_utc")] public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    [JsonPropertyName("start_with_windows")] public bool StartWithWindows { get; set; }
    [JsonPropertyName("monitors")] public List<MonitorProfile> Monitors { get; set; } = new();
    [JsonPropertyName("selected_monitor_id")] public string SelectedMonitorId { get; set; } = "";
}
