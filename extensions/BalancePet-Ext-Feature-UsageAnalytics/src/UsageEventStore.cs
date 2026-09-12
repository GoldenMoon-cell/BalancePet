using System.IO;

namespace BalancePet.UsageAnalytics;

public sealed class UsageEventStore
{
    private const int MaxEvents = 200_000;
    private const int MaxLineLength = 32_768;
    private readonly string _directory;

    public UsageEventStore(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? GetDefaultDirectory() : directory;
    }

    public string DirectoryPath => _directory;

    public IReadOnlyList<UsageEvent> ReadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<UsageEvent>();
        var result = new List<UsageEvent>();
        var eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_directory, "usage-events*.ndjson", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToArray();
        }
        catch (IOException) { return Array.Empty<UsageEvent>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<UsageEvent>(); }

        foreach (var path in files)
        {
            try
            {
                using var reader = new StreamReader(path);
                while (result.Count < MaxEvents)
                {
                    var line = reader.ReadLine();
                    if (line is null) break;
                    if (line.Length == 0 || line.Length > MaxLineLength) continue;
                    if (UsageEvent.TryParse(line, out var parsed) && parsed is not null && eventIds.Add(parsed.EventId))
                        result.Add(parsed);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(value => value.OccurredAt).ToArray();
    }

    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet");
}
