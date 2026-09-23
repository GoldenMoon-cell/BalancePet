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
