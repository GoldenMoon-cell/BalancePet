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
        string spentCurrency)
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
            SpentCurrency = CleanCurrency(spentCurrency)
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

    private sealed class NotificationStateDocument
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = NotificationStateStore.Schema;
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
