using System.IO;

namespace BalancePet.UsageAnalytics;

public sealed class UsageEventStore
{
    private const int MaxEvents = 200_000;
    private const int MaxLineLength = 128 * 1024;
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsageEvent> _eventsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _eventSources = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<UsageEvent>? _snapshot;

    public UsageEventStore(string directory)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? GetDefaultDirectory() : directory;
    }

    public string DirectoryPath => _directory;

    public IReadOnlyList<UsageEvent> ReadAll()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                ClearCache();
                return Array.Empty<UsageEvent>();
            }

            FileInfo[] files;
            try
            {
                files = Directory.EnumerateFiles(_directory, "usage-events*.ndjson", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(value => value.LastWriteTimeUtc)
                    .Take(32)
                    .OrderBy(value => value.LastWriteTimeUtc)
                    .ToArray();
            }
            catch (IOException) { return _snapshot ?? Array.Empty<UsageEvent>(); }
            catch (UnauthorizedAccessException) { return _snapshot ?? Array.Empty<UsageEvent>(); }

            var selectedPaths = files.Select(value => value.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stalePath in _files.Keys.Where(path => !selectedPaths.Contains(path)).ToArray())
                RemoveFile(stalePath);

            var changed = false;
            foreach (var file in files)
            {
                var path = file.FullName;
                if (_files.TryGetValue(path, out var cached)
                    && cached.Length == file.Length
                    && cached.LastWriteUtc == file.LastWriteTimeUtc)
                {
                    continue;
                }

                RemoveFile(path);
                var eventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var reader = new StreamReader(path);
                    while (reader.ReadLine() is { } line)
                    {
                        if (line.Length == 0 || line.Length > MaxLineLength) continue;
                        if (!UsageEvent.TryParse(line, out var parsed) || parsed is null) continue;
                        _eventsById[parsed.EventId] = parsed;
                        _eventSources[parsed.EventId] = path;
                        eventIds.Add(parsed.EventId);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                _files[path] = new CachedFile(file.Length, file.LastWriteTimeUtc, eventIds);
                changed = true;
            }

            if (!changed && _snapshot is not null) return _snapshot;
            TrimToLimit();
            _snapshot = _eventsById.Values.OrderByDescending(value => value.OccurredAt).ToArray();
            return _snapshot;
        }
    }

    private void RemoveFile(string path)
    {
        if (!_files.Remove(path, out var cached)) return;
        foreach (var eventId in cached.EventIds)
        {
            if (_eventSources.TryGetValue(eventId, out var source)
                && string.Equals(source, path, StringComparison.OrdinalIgnoreCase))
            {
                _eventSources.Remove(eventId);
                _eventsById.Remove(eventId);
            }
        }
        _snapshot = null;
    }

    private void TrimToLimit()
    {
        if (_eventsById.Count <= MaxEvents) return;
        var keep = _eventsById.Values
            .OrderByDescending(value => value.OccurredAt)
            .Take(MaxEvents)
            .Select(value => value.EventId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var eventId in _eventsById.Keys.Where(value => !keep.Contains(value)).ToArray())
        {
            _eventsById.Remove(eventId);
            _eventSources.Remove(eventId);
        }
    }

    private void ClearCache()
    {
        _files.Clear();
        _eventsById.Clear();
        _eventSources.Clear();
        _snapshot = Array.Empty<UsageEvent>();
    }

    private sealed record CachedFile(long Length, DateTime LastWriteUtc, HashSet<string> EventIds);

    public static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet");
}
