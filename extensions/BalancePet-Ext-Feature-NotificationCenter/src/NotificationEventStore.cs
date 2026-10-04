using System.IO;
using System.Text.Json;

namespace BalancePet.NotificationCenter;

public sealed class NotificationEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory;

    public NotificationEventStore(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet")
            : directory;
    }

    /// <summary>
    /// How many events of each category to keep, newest first within the category.
    /// </summary>
    /// <remarks>
    /// Every category has a limit. They differ because the categories differ in how much of
    /// them anyone reads: interaction is the noisiest and only feeds the ring, while an account
    /// change or a system message is rare and is the reason the window was opened at all.
    ///
    /// A single shared limit is what this replaces, and a shared limit is a limit the noisiest
    /// category sets. The stream on the machine this was written on holds 2199 events -- 1187
    /// interaction and 688 task -- so a newest-first cap of 500 between them left the account
    /// section with nothing, the system section with nothing, balance and refresh with one entry
    /// each, and three of the eight changelog entries. The changelog was the visible symptom
    /// rather than the whole of it: an entry carries its publication date rather than the moment
    /// it was fetched, so the oldest entry is also the oldest event in the file.
    ///
    /// A category the table does not name still gets a limit, rather than being let through: a
    /// category nobody has thought about yet is not a category that should be unbounded.
    /// </remarks>
    private static readonly Dictionary<string, int> CategoryLimits = new(StringComparer.Ordinal)
    {
        ["notice"] = 400,        // 更新记录：短，而且是从头读的，所以给得最宽
        ["interaction"] = 200,   // 最吵；只喂环绕层，不进阅读界面
        ["task"] = 200,
        ["account"] = 100,
        ["balance"] = 100,
        ["refresh"] = 50,
        ["system"] = 50,
    };

    /// <summary>A limit for a category the table above does not name.</summary>
    private const int DefaultCategoryLimit = 100;
    public IReadOnlyList<NotificationEvent> ReadAll()
    {
        try
        {
            if (!Directory.Exists(_directory)) return [];
            var files = Directory.EnumerateFiles(_directory, "notification-events*.ndjson")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(12)
                .ToArray();
            var rows = new List<NotificationEvent>();
            foreach (var file in files)
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var row = JsonSerializer.Deserialize<NotificationEvent>(line, JsonOptions);
                        if (row is not null) rows.Add(row);
                    }
                    catch (JsonException) { }
                }
            }

            var kept = new List<NotificationEvent>();
            foreach (var group in rows.GroupBy(row => row.Category ?? ""))
            {
                var cap = CategoryLimits.TryGetValue(group.Key, out var named) ? named : DefaultCategoryLimit;
                kept.AddRange(group.OrderByDescending(row => row.OccurredAt).Take(cap));
            }
            return kept.OrderByDescending(row => row.OccurredAt).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
    public string DirectoryPath => _directory;
}
