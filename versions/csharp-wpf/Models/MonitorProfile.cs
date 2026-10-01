using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Models;

/// <summary>
/// A single balance endpoint. Credentials remain encrypted in TokenBlob.
/// </summary>
public sealed class MonitorProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "默认账户";
    [JsonPropertyName("preset_id")] public string PresetId { get; set; } = "custom";
    [JsonPropertyName("site_url")] public string SiteUrl { get; set; } = "";
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";
    // Optional read-only per-request usage endpoint. When set, the main
    // process probes this endpoint directly with the configured token/session
    // before falling back to the built-in New API endpoint list.
    [JsonPropertyName("usage_detail_endpoint")] public string UsageDetailEndpoint { get; set; } = "";
    [JsonPropertyName("auth_mode")] public string AuthMode { get; set; } = "bearer";
    [JsonPropertyName("header_name")] public string HeaderName { get; set; } = "Authorization";
    [JsonPropertyName("token_blob")] public string TokenBlob { get; set; } = "";
    // Optional read-only web session used only for per-request usage details
    // when the relay does not expose those records through its API token.
    [JsonPropertyName("web_session_blob")] public string WebSessionBlob { get; set; } = "";
    // Browser used by the optional local database reader. The extension path is browser-agnostic.
    [JsonPropertyName("browser_session_browser")] public string BrowserSessionBrowser { get; set; } = "edge";
    [JsonPropertyName("balance_path")] public string BalancePath { get; set; } = "balance";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
    [JsonPropertyName("refresh_seconds")] public int RefreshSeconds { get; set; } = 60;
    [JsonPropertyName("auto_refresh_enabled")] public bool AutoRefreshEnabled { get; set; } = true;
    [JsonPropertyName("low_threshold")] public double LowThreshold { get; set; } = 5;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    /// <summary>
    /// The user's declaration that this account is billed by subscription rather
    /// than per request.
    /// </summary>
    /// <remarks>
    /// Nothing in an account's configuration can reveal this: a subscription key
    /// and a pay-as-you-go key look identical, and the vendor's balance endpoint
    /// simply returns nothing useful for a subscription. Cost reporting therefore
    /// has to be told, or it will keep describing a subscription account as an
    /// account whose balance never moved.
    ///
    /// Stated by the user rather than inferred so that it works for every
    /// subscription, including ones whose client never reports a plan type.
    /// </remarks>
    [JsonPropertyName("is_subscription")] public bool IsSubscription { get; set; }
}
