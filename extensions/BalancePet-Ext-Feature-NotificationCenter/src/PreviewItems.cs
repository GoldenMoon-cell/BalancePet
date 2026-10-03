using System.IO;
using System.Text.Json;

namespace BalancePet.NotificationCenter;

/// <summary>
/// What the ring shows in a preview render.
/// </summary>
/// <remarks>
/// Read from a file so a review can look at content that does not exist yet — the point of
/// reviewing the ring is usually to decide what it should say, and the four slots it fills
/// today are exactly what is under discussion. With no file, the four current slots are
/// used with plausible values, which is what a reviewer wants to see first: what it looks
/// like now.
/// </remarks>
internal static class PreviewItems
{
    public static IReadOnlyList<NotificationBubble> Load(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var items = new List<NotificationBubble>();
                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    var text = entry.TryGetProperty("text", out var textValue) ? textValue.GetString() ?? "" : "";
                    var detail = entry.TryGetProperty("detail", out var detailValue) ? detailValue.GetString() ?? "" : "";
                    var kind = entry.TryGetProperty("kind", out var kindValue) ? kindValue.GetString() ?? "system" : "system";
                    var shortForm = entry.TryGetProperty("short", out var shortValue) ? shortValue.GetString() ?? "" : "";
                    if (text.Length > 0) items.Add(new NotificationBubble(text, detail, kind, shortForm));
                }
                if (items.Count > 0) return items;
            }
            catch (Exception error)
            {
                Console.WriteLine($"预览内容读取失败，改用默认：{error.Message}");
            }
        }

        return Current();
    }

    /// <summary>What the ring shows today: four fixed slots, with a plausible account.</summary>
    internal static IReadOnlyList<NotificationBubble> Current() =>
    [
        new NotificationBubble("余额 42.80 CNY", "上次余额 35.10 · 本次消耗 7.70", "balance", "42.80 CNY"),
        new NotificationBubble("当前登录方式 · 官方登录", "官方 API · DeepSeek", "account", "官方登录"),
        new NotificationBubble("DeepSeek 工作中", "正在处理", "task", "工作中"),
        new NotificationBubble("当前版本 · 1.5.1", "BalancePet", "system", "v1.5.1")
    ];
}
