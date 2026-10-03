using System.Text.Json.Serialization;

namespace BalancePet.NotificationCenter;

public sealed class NotificationEvent
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "balancepet.notifications.v1";
    [JsonPropertyName("event_id")] public string EventId { get; set; } = "";
    [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "interaction";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("amount")] public string Amount { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    /// <summary>
    /// Where a changelog entry points. Absent on every other kind of message, which is why
    /// it is optional and why only an entry gets an action in the list.
    /// </summary>
    [JsonPropertyName("url")] public string? Url { get; set; }
}
