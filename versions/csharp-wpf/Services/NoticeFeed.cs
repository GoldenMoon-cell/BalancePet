using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>One published note about something that changed outside a release.</summary>
public sealed record NoticeItem(int Seq, string Date, string Area, string Title, string Summary, string Url);

/// <summary>
/// Notes published between releases: a specification gaining a section, a document being
/// corrected, a published appearance's lines being rewritten.
/// </summary>
/// <remarks>
/// These exist because the alternative was worse. Everything the program tells the user
/// about travels in a release, and a release is a version number, two artifacts, a
/// changelog and a download — a price that only makes sense for changes to the program.
/// A specification that gained a section is not a new version of anything, so it went
/// unannounced, and the only way to learn about it was to happen to look at the
/// repository. This is the channel for exactly that gap, and deliberately for nothing
/// else: an entry here says "a document changed", never "a new version exists", which is
/// what the update check is for.
///
/// The entries are written by hand rather than derived. A file hash can say that
/// something changed; it cannot say what, and "what" is the entire message.
/// </remarks>
public static class NoticeFeed
{
    public const string Url = "https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet/main/notices.json";

    public const int CurrentSchemaVersion = 1;

    /// <summary>Ceiling for the document, which is short prose and grows slowly.</summary>
    private const long MaxBytes = 256 * 1024;

    /// <summary>Beyond this the document is not being used as a changelog.</summary>
    private const int MaxItems = 400;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly string[] RequiredText = ["date", "area", "title"];

    /// <summary>Cached like the catalogs, so a launch without a network still knows.</summary>
    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "notices.json");

    private static readonly object Gate = new();
    private static IReadOnlyList<NoticeItem> _items = Array.Empty<NoticeItem>();

    /// <summary>Newest first, which is the order they are read in.</summary>
    public static IReadOnlyList<NoticeItem> All
    {
        get { lock (Gate) { return _items; } }
    }

    /// <summary>
    /// The highest sequence number published, or 0 when nothing is loaded. Recording this
    /// is what makes a fresh installation stay quiet: there is no history to catch up on,
    /// and everything published before the program was installed has already been read by
    /// definition.
    /// </summary>
    public static int NewestSeq => All.Count == 0 ? 0 : All.Max(item => item.Seq);

    /// <summary>Everything published after the given sequence number, newest first.</summary>
    public static IReadOnlyList<NoticeItem> NewerThan(int seenSeq)
        => All.Where(item => item.Seq > seenSeq).ToArray();

    /// <summary>
    /// What still has to be handed on, oldest first.
    /// </summary>
    /// <remarks>
    /// The order a stream wants, which is the reverse of the order a person wants: the
    /// feed is read newest first, and a file whose lines are appended should read in the
    /// order things happened. The watermark is what makes this answer "nothing" the second
    /// time it is asked — the caller advances it — so an entry is handed on once rather
    /// than on every half-hourly fetch.
    /// </remarks>
    public static IReadOnlyList<NoticeItem> RecordableFrom(int watermark)
        => NewerThan(watermark).Reverse().ToArray();

    /// <summary>
    /// Replaces the published notes with the contents of a document, returning whether it
    /// could be used at all.
    /// </summary>
    /// <remarks>
    /// An unrecognised document replaces nothing. The cached copy is then still in force,
    /// so a feed written against a schema this build does not know degrades to notes that
    /// are merely older rather than to a window that has silently emptied itself.
    /// </remarks>
    public static bool Publish(string json)
    {
        var parsed = Parse(json);
        if (parsed is null) return false;
        lock (Gate) { _items = parsed; }
        return true;
    }

    /// <summary>Loads the cached copy, if any. Called before the first draw.</summary>
    public static void LoadCache()
    {
        try
        {
            if (File.Exists(CachePath)) Publish(File.ReadAllText(CachePath));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
    }

    /// <summary>
    /// Fetches the document and, when it is usable, publishes and caches it.
    /// </summary>
    /// <remarks>
    /// Silent on failure, for the same reason the served lines are: there is nothing to
    /// say and nothing to do. The window shows whatever the last good copy held, and a
    /// later refresh retries. Reporting it would put an error about a changelog in front
    /// of someone who did not ask for either.
    ///
    /// Fetched through <see cref="GitHubContentReader"/> so the mirror is asked as well.
    /// This was the last document still going straight to GitHub's raw host, and the one
    /// where it shows most: a note that needs a manual refresh to appear is a note nobody
    /// reads, and on a network that refuses that host the fetch failed on every launch
    /// while the window kept showing whatever the cache held.
    /// </remarks>
    public static async Task RefreshAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            var fetched = await GitHubContentReader.DownloadAsync(
                http, Url, MaxBytes, "application/json",
                "BalancePet-Notices/1.0", cancellationToken);

            // Published before caching: an unwritable cache directory costs the next
            // launch a refetch, while refusing the document would cost this session the
            // notes for no reason.
            if (!Publish(fetched.Text)) return;

            var directory = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(CachePath, fetched.Text);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or OperationCanceledException or InvalidOperationException or InvalidDataException)
        {
        }
    }

    /// <summary>
    /// Whether a notices document can be used at all. Exposed so the fallback can be
    /// tested directly.
    /// </summary>
    public static bool IsReadable(string json) => Parse(json) is not null;

    private static IReadOnlyList<NoticeItem>? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) return null;
        try
        {
            var document = JsonSerializer.Deserialize<NoticeFile>(json, Options);
            if (document is null || document.SchemaVersion != CurrentSchemaVersion) return null;
            if (document.Notices is null) return null;

            var items = new List<NoticeItem>();
            var seen = new HashSet<int>();
            foreach (var entry in document.Notices)
            {
                if (entry is null) continue;
                // A run of entries with the same sequence number would make the watermark
                // ambiguous: recording that number would mark the rest of the run as read.
                // Keeping the first is arbitrary but consistent, and a duplicate is a
                // publisher's mistake either way.
                if (entry.Seq <= 0 || !seen.Add(entry.Seq)) continue;
                if (RequiredText.Any(field => string.IsNullOrWhiteSpace(entry.Text(field)))) continue;
                if (!IsSafeUrl(entry.Url)) continue;
                items.Add(new NoticeItem(entry.Seq, entry.Date!.Trim(), entry.Area!.Trim(),
                    entry.Title!.Trim(), (entry.Summary ?? "").Trim(), entry.Url!.Trim()));
            }

            if (items.Count > MaxItems) return null;
            return items.OrderByDescending(item => item.Seq).ToArray();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The link is opened by the shell when the user asks for it, so it has to be an
    /// https address rather than whatever the document felt like putting there.
    /// </summary>
    private static bool IsSafeUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;

    private sealed class NoticeFile
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("notices")] public List<Entry?>? Notices { get; set; }

        internal sealed class Entry
        {
            [JsonPropertyName("seq")] public int Seq { get; set; }
            [JsonPropertyName("date")] public string? Date { get; set; }
            [JsonPropertyName("area")] public string? Area { get; set; }
            [JsonPropertyName("title")] public string? Title { get; set; }
            [JsonPropertyName("summary")] public string? Summary { get; set; }
            [JsonPropertyName("url")] public string? Url { get; set; }

            public string? Text(string field) => field switch
            {
                "date" => Date,
                "area" => Area,
                "title" => Title,
                _ => null
            };
        }
    }
}
