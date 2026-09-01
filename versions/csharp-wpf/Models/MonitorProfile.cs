using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Models;

/// <summary>
/// A single balance endpoint. Credentials remain encrypted in TokenBlob.
/// </summary>
public sealed class MonitorProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "默认账户";
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";
    [JsonPropertyName("auth_mode")] public string AuthMode { get; set; } = "bearer";
    [JsonPropertyName("header_name")] public string HeaderName { get; set; } = "Authorization";
    [JsonPropertyName("token_blob")] public string TokenBlob { get; set; } = "";
    [JsonPropertyName("balance_path")] public string BalancePath { get; set; } = "balance";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
    [JsonPropertyName("refresh_seconds")] public int RefreshSeconds { get; set; } = 60;
    [JsonPropertyName("low_threshold")] public double LowThreshold { get; set; } = 5;
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}
