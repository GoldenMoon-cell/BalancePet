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

    public IReadOnlyList<NotificationEvent> ReadAll(int limit = 500)
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
            return rows.OrderByDescending(value => value.OccurredAt).Take(limit).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public string DirectoryPath => _directory;
}
