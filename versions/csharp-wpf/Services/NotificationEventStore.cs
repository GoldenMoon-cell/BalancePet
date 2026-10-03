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
    private readonly string _directory;
    private bool _disposed;

    /// <summary>
    /// Writes to the documented path unless told otherwise, which only the self-test does:
    /// the resulting file is read by extensions at a fixed location, so nothing else has
    /// any business redirecting it.
    /// </summary>
    public NotificationEventStore(string? directory = null)
        => _directory = directory ?? GetDefaultDirectory();

    public static string GetDefaultDirectory() => UsageEventBridge.GetDefaultDirectory();
    public static string GetEventPath() => Path.Combine(GetDefaultDirectory(), "notification-events.ndjson");
    private string EventPath => Path.Combine(_directory, "notification-events.ndjson");

    /// <summary>
    /// Appends one bubble summary. Returns the write, which most callers ignore: a bubble
    /// is transient and losing its record changes nothing a user can see.
    /// </summary>
    public Task Record(string title, string amount, string detail)
    {
        if (_disposed) return Task.CompletedTask;
        return AppendAsync(new NotificationEventData
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

    /// <summary>
    /// Writes one changelog entry into the same stream the message centre already reads.
    /// </summary>
    /// <remarks>
    /// The changelog used to be a window of the host's own, showing the same kind of thing
    /// the message centre exists to show. Merging them means the host stops presenting and
    /// keeps producing: it still fetches the feed, because the pet's bubble has to mention
    /// new entries, and it writes each one here once.
    ///
    /// The summary is longer than a bubble's detail line — a bubble says what happened, an
    /// entry explains it — so the cap is higher. Nothing sensitive is involved either way:
    /// these are the sentences published in the feed.
    ///
    /// The write is returned rather than dropped, and the caller waits for it before
    /// recording that the entry has been handed over. Writing the watermark first loses the
    /// entry for good: the queued append is discarded when the store is disposed at exit,
    /// and the next launch sees a watermark saying it was already written.
    ///
    /// The event id is derived from the sequence number, so an entry written twice — a
    /// watermark saved after the write but before the exit, say — is recognisable as the
    /// same entry rather than as a second one.
    /// </remarks>
    public Task RecordNotice(int seq, string date, string area, string title, string summary, string url)
    {
        if (_disposed) return Task.CompletedTask;
        if (!int.TryParse(date.Replace("-", ""), out var stamp) || date.Length != 10)
            stamp = int.Parse(DateTime.Now.ToString("yyyyMMdd"));

        return AppendAsync(new NotificationEventData
        {
            Schema = "balancepet.notifications.v1",
            EventId = $"notice-{seq}",
            // The entry's own date rather than now: a changelog is read in publication
            // order, and the feed's dates are the only ones that order it correctly when a
            // fresh installation writes a backlog all at once.
            OccurredAt = new DateTimeOffset(
                stamp / 10000, stamp / 100 % 100, stamp % 100, 0, 0, 0, TimeSpan.Zero),
            Category = NoticeCategory,
            // The area -- 规范, 文档, 在线内容 -- rides in the amount slot, which is the
            // short piece of text shown beside a title. A changelog entry has no amount.
            Title = Clean(title, 80),
            Amount = Clean(area, 80),
            Detail = Clean(summary, 640),
            Url = Clean(url, 400)
        });
    }

    /// <summary>The category a changelog entry is written under.</summary>
    public const string NoticeCategory = "notice";

    private async Task AppendAsync(NotificationEventData data)
    {
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(_directory);
                var path = EventPath;
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
        // Where a changelog entry points. Absent on everything else, and optional in the
        // published shape: a bubble summary has nowhere to send anybody.
        [JsonPropertyName("url")] public string? Url { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
