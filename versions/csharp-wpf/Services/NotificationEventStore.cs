using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Persists the sanitized summaries shown in the pet bubble for an
/// out-of-process notification-center extension. Secrets and raw provider
/// payloads never enter this stream.
/// </summary>
public sealed class NotificationEventStore : IDisposable
{
    private const long MaxFileBytes = 8L * 1024 * 1024;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public static string GetDefaultDirectory() => UsageEventBridge.GetDefaultDirectory();
    public static string GetEventPath() => Path.Combine(GetDefaultDirectory(), "notification-events.ndjson");

    public void Record(string title, string amount, string detail)
    {
        if (_disposed) return;
        _ = AppendAsync(new NotificationEventData
        {
            Schema = "balancepet.notifications.v1",
            EventId = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.Now,
            Category = Classify(title),
            Title = Clean(title, 80),
            Amount = Clean(amount, 80),
            Detail = Clean(detail, 240)
        });
    }

    private async Task AppendAsync(NotificationEventData data)
    {
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var directory = GetDefaultDirectory();
                Directory.CreateDirectory(directory);
                var path = GetEventPath();
                RotateIfNeeded(path);
                var line = JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine;
                await File.AppendAllTextAsync(path, line, new UTF8Encoding(false)).ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ObjectDisposedException) { }
    }

    private static string Classify(string title)
    {
        if (title.Contains("余额", StringComparison.OrdinalIgnoreCase) || title.Contains("balance", StringComparison.OrdinalIgnoreCase)) return "balance";
        if (title.Contains("刷新", StringComparison.OrdinalIgnoreCase) || title.Contains("查询", StringComparison.OrdinalIgnoreCase) || title.Contains("refresh", StringComparison.OrdinalIgnoreCase)) return "refresh";
        if (title.Contains("任务", StringComparison.OrdinalIgnoreCase) || title.Contains("工作", StringComparison.OrdinalIgnoreCase) || title.Contains("task", StringComparison.OrdinalIgnoreCase)) return "task";
        if (title.Contains("账户", StringComparison.OrdinalIgnoreCase) || title.Contains("API", StringComparison.OrdinalIgnoreCase) || title.Contains("登录", StringComparison.OrdinalIgnoreCase) || title.Contains("account", StringComparison.OrdinalIgnoreCase)) return "account";
        if (title.Contains("更新", StringComparison.OrdinalIgnoreCase) || title.Contains("扩展", StringComparison.OrdinalIgnoreCase) || title.Contains("update", StringComparison.OrdinalIgnoreCase)) return "system";
        return "interaction";
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes) return;
            var rotated = Path.Combine(Path.GetDirectoryName(path)!, $"notification-events-{DateTime.UtcNow:yyyyMMddHHmmssfff}.ndjson");
            File.Move(path, rotated, true);
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _writeLock.Dispose();
    }

    private sealed class NotificationEventData
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "balancepet.notifications.v1";
        [JsonPropertyName("event_id")] public string EventId { get; set; } = "";
        [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = "interaction";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("amount")] public string Amount { get; set; } = "";
        [JsonPropertyName("detail")] public string Detail { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
