using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.UsageAnalytics;

public sealed class UsageEvent
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    [JsonPropertyName("event_id")] public string EventId { get; set; } = "";
    [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("input_tokens")] public long? InputTokens { get; set; }
    [JsonPropertyName("output_tokens")] public long? OutputTokens { get; set; }
    [JsonPropertyName("cache_read_tokens")] public long? CacheReadTokens { get; set; }
    [JsonPropertyName("cache_write_tokens")] public long? CacheWriteTokens { get; set; }
    [JsonPropertyName("duration_ms")] public long? DurationMs { get; set; }
    [JsonPropertyName("time_to_first_token_ms")] public long? TimeToFirstTokenMs { get; set; }
    [JsonPropertyName("tool_calls")] public int? ToolCalls { get; set; }
    [JsonPropertyName("steps")] public int? Steps { get; set; }

    public bool IsValid()
    {
        return Schema == "balancepet.usage.v1"
            && !string.IsNullOrWhiteSpace(EventId) && EventId.Length <= 80
            && Kind == "llm_request"
            && !string.IsNullOrWhiteSpace(Provider) && Provider.Length <= 64
            && EventId.All(character => !char.IsControl(character))
            && Provider.All(character => !char.IsControl(character))
            && NonNegative(InputTokens) && NonNegative(OutputTokens)
            && NonNegative(CacheReadTokens) && NonNegative(CacheWriteTokens)
            && NonNegative(DurationMs) && NonNegative(TimeToFirstTokenMs)
            && NonNegative(ToolCalls) && NonNegative(Steps)
            && (InputTokens is null || InputTokens <= 10_000_000_000)
            && (OutputTokens is null || OutputTokens <= 10_000_000_000);
    }

    public static bool TryParse(string line, out UsageEvent? value)
    {
        value = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<UsageEvent>(line, Options);
            if (parsed is null || !parsed.IsValid()) return false;
            value = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool NonNegative(long? value) => value is null or >= 0;
    private static bool NonNegative(int? value) => value is null or >= 0;
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}
