using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>One thing an appearance says when it is idle, clicked, or touched.</summary>
public sealed record PetLine(
    string Label,
    string Amount,
    string Hint,
    string? EnglishLabel = null,
    string? EnglishAmount = null,
    string? EnglishHint = null)
{
    /// <summary>Returns the authored English variant when requested, otherwise the original line.</summary>
    public PetLine ForLanguage(string? language)
    {
        if (!AppLocalization.IsEnglish(language)) return this;
        return this with
        {
            Label = string.IsNullOrWhiteSpace(EnglishLabel) ? Label : EnglishLabel,
            Amount = string.IsNullOrWhiteSpace(EnglishAmount) ? Amount : EnglishAmount,
            Hint = string.IsNullOrWhiteSpace(EnglishHint) ? Hint : EnglishHint
        };
    }
}

/// <summary>
/// The lines an appearance speaks, read from <c>lines.json</c> beside its artwork.
/// </summary>
/// <remarks>
/// The lines belong to the character, so they travel with the package rather than
/// living in the program: an appearance added later can have its own voice without a
/// new build, and the program no longer carries copy for characters it does not ship.
///
/// The file sits next to the nine state images rather than inside the manifest, which
/// means the same lookup finds it for an appearance that ships inside the application
/// and one that arrived as a package, and a host that knows nothing about it simply
/// ignores it — the same arrangement the optional animation frames use.
///
/// Every category falls back to a neutral set when the file is absent or incomplete,
/// because a third-party appearance has no lines of its own and must not be given
/// another character's name.
/// </remarks>
public static class PetLineCatalog
{
    public const string FileName = "lines.json";
    public const int CurrentSchemaVersion = 2;

    private static bool IsSupportedSchemaVersion(int version) => version is 1 or CurrentSchemaVersion;

    /// <summary>Anything longer than this is not a lines file, whatever it claims.</summary>
    private const long MaxBytes = 128 * 1024;

    /// <summary>Enough for a generous set; beyond it the file is not being used as intended.</summary>
    private const int MaxLinesPerCategory = 24;

    /// <summary>
    /// Ceiling for the document the appearance repository serves, which carries every
    /// appearance at once. Generous, because it is one file for all of them rather than
    /// one per character.
    /// </summary>
    /// <remarks>
    /// Public because the fetch applies it as a limit on the download as well: a ceiling
    /// that is only checked after the whole body has been read is a ceiling that does not
    /// stop anything from filling memory.
    /// </remarks>
    public const long MaxRemoteBytes = 512 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Keyed by asset directory, so a package replacing another is picked up.</summary>
    private static readonly Dictionary<string, LineFile?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>
    /// Lines served by the appearance repository, keyed by appearance id.
    /// </summary>
    /// <remarks>
    /// A second source, consulted before the package's own file, and it exists because of
    /// what a line change used to cost. The lines were only ever inside the package, and a
    /// package is mostly artwork: correcting one word meant republishing an appearance,
    /// bumping its version, and pushing every installation through a download of several
    /// megabytes to receive a few hundred bytes. Measured on the published set, updating
    /// all fourteen appearances to gain their lines was 147 MB of transfer for 36 KB of
    /// text.
    ///
    /// The file inside the package stays, and is what an installation that has never
    /// reached the network uses. So a package still speaks for itself offline, and a third
    /// party's appearance is unaffected either way because this document only ever names
    /// the appearances published by the project.
    /// </remarks>
    private static Dictionary<string, LineFile>? _remote;

    /// <summary>The categories an appearance may speak in, each with its neutral fallback.</summary>
    private static readonly Dictionary<string, PetLine[]> Neutral = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inactive"] = new[]
        {
            new PetLine("还在待机", "慢慢来", "需要时点我一下就好"),
            new PetLine("没有走远", "我还醒着", "余额变化我会继续看着"),
            new PetLine("安静待机中", "小憩一下", "余额和心情都留一点余量")
        },
        ["bubble"] = new[]
        {
            new PetLine("在看着余额", "放心吧", "余额变动会告诉你"),
            new PetLine("被碰了一下", "轻一点", "点击角色可以刷新余额"),
            new PetLine("找到我了", "在呢", "点击角色可以刷新余额"),
            new PetLine("记下了", "收到", "完成后会显示本次消耗"),
            new PetLine("今天也要稳住", "嗯哼", "别忘了看看今日消耗")
        },
        ["streak"] = new[]
        {
            new PetLine("被发现了", "眨眨眼", "连续互动彩蛋"),
            new PetLine("四下连击", "四连击", "这次真的抓到我啦"),
            new PetLine("先歇一下", "缓一缓", "连续互动太快啦")
        },
        // Shared by every touched part: without the appearance's own file there is no
        // way to know whether a hair line suits the character being poked.
        ["touch"] = new[]
        {
            new PetLine("被戳到了", "在呢", "点击可以刷新余额"),
            new PetLine("收到了", "嗯", "余额变化会及时告诉你"),
            new PetLine("今天也要稳住", "嗯哼", "别忘了看看今日消耗")
        }
    };

    /// <summary>
    /// The lines for one moment: the appearance's own when it has them, the neutral set
    /// otherwise.
    /// </summary>
    /// <param name="style">Appearance id, as stored in settings.</param>
    /// <param name="category">One of <c>inactive</c>, <c>bubble</c>, <c>streak</c>, <c>touch</c>.</param>
    /// <param name="kind">For <c>touch</c>, the part that was touched.</param>
    public static IReadOnlyList<PetLine> Resolve(string? style, string category, string? kind = null)
    {
        var lines = FromFile(style, category, kind);
        if (lines.Count > 0) return lines;
        return Neutral.TryGetValue(category, out var fallback) ? fallback : Array.Empty<PetLine>();
    }

    private static IReadOnlyList<PetLine> FromFile(string? style, string category, string? kind)
    {
        var remote = LoadRemote(style);
        var package = LoadPackage(style);
        var source = remote ?? package;
        var entries = SelectEntries(source, category, kind);
        // A previously cached v1 online document has no English copy. Keep its updated
        // Chinese lines, but borrow the matching package translation until the online
        // catalog is refreshed; line order is stable within a published appearance.
        var packageEntries = remote is null ? null : SelectEntries(package, category, kind);
        return ToLines(entries, packageEntries);
    }

    private static List<LineFile.Entry>? SelectEntries(LineFile? file, string category, string? kind)
    {
        if (file is null) return null;
        return category.ToLowerInvariant() switch
        {
            "inactive" => file.Inactive,
            "bubble" => file.Bubble,
            "streak" => file.Streak,
            // A part with no lines of its own still falls back to the neutral set rather
            // than to another part's: "hair" copy on an arm reads as a mistake.
            "touch" => kind is not null && (file.Touch?.TryGetValue(kind, out var zoned) ?? false) ? zoned : null,
            _ => null
        };
    }

    private static IReadOnlyList<PetLine> ToLines(List<LineFile.Entry>? entries, List<LineFile.Entry>? packageFallback = null)
    {
        if (entries is null || entries.Count == 0) return Array.Empty<PetLine>();
        var lines = new List<PetLine>(Math.Min(entries.Count, MaxLinesPerCategory));
        for (var index = 0; index < entries.Count && lines.Count < MaxLinesPerCategory; index++)
        {
            var entry = entries[index];
            // A line with nothing in it would render as an empty bubble, so a partially
            // filled entry is dropped rather than shown.
            if (string.IsNullOrWhiteSpace(entry.Label) && string.IsNullOrWhiteSpace(entry.Amount)) continue;
            var fallbackEnglish = packageFallback is not null && index < packageFallback.Count
                ? packageFallback[index].English
                : null;
            lines.Add(new PetLine(
                entry.Label ?? "", entry.Amount ?? "", entry.Hint ?? "",
                EnglishOrFallback(entry.English?.Label, fallbackEnglish?.Label),
                EnglishOrFallback(entry.English?.Amount, fallbackEnglish?.Amount),
                EnglishOrFallback(entry.English?.Hint, fallbackEnglish?.Hint)));
        }
        return lines;
    }

    private static string? EnglishOrFallback(string? english, string? packageEnglish)
        => string.IsNullOrWhiteSpace(english) ? packageEnglish : english;

    private static LineFile? LoadRemote(string? style)
    {
        var id = PetStyleCatalog.NormalizeId(style);
        lock (Gate) return _remote is not null && _remote.TryGetValue(id, out var served) ? served : null;
    }

    private static LineFile? LoadPackage(string? style)
    {
        var id = PetStyleCatalog.NormalizeId(style);
        string directory;
        try { directory = PetStyleCatalog.ResolveAssetDirectory(id); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { return null; }

        lock (Gate)
        {
            if (Cache.TryGetValue(directory, out var cached)) return cached;
        }

        var parsed = Read(Path.Combine(directory, FileName));
        lock (Gate) { Cache[directory] = parsed; }
        return parsed;
    }

    /// <summary>
    /// Replaces the served lines with the contents of a document, returning whether it
    /// could be used at all.
    /// </summary>
    /// <remarks>
    /// A document written against a schema this build does not know replaces nothing. The
    /// package copies are then still in play, so an unrecognised document degrades to the
    /// lines that shipped rather than to an empty set — and never to guessed words.
    /// </remarks>
    public static bool PublishRemote(string json)
    {
        var document = ParseRemote(json);
        if (document is null) return false;
        lock (Gate) { _remote = document; }
        return true;
    }

    private static Dictionary<string, LineFile>? ParseRemote(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxRemoteBytes) return null;
        try
        {
            var document = JsonSerializer.Deserialize<ServedFile>(json, Options);
            if (document is null || !IsSupportedSchemaVersion(document.SchemaVersion)) return null;
            if (document.Lines is null) return null;

            var map = new Dictionary<string, LineFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in document.Lines)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) continue;
                map[pair.Key.Trim()] = pair.Value;
            }
            return map.Count == 0 ? null : map;
        }
        catch (JsonException) { return null; }
    }

    private static LineFile? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaxBytes) return null;
            return Parse(File.ReadAllText(path));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Whether a lines document can be used at all.
    /// </summary>
    /// <remarks>
    /// Exposed so the fallback can be tested directly. A file written against a schema
    /// this build does not know is not read at all: guessing at an unknown shape would
    /// put wrong words in a character's mouth, while the neutral lines are merely
    /// generic. Either way the pet still draws.
    /// </remarks>
    public static bool IsReadable(string json) => Parse(json) is not null;

    private static LineFile? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes) return null;
        try
        {
            var document = JsonSerializer.Deserialize<LineFile>(json, Options);
            return document is not null && IsSupportedSchemaVersion(document.SchemaVersion) ? document : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The document the appearance repository serves: every appearance's lines, keyed by
    /// the same id a saved setting stores.
    /// </summary>
    private sealed class ServedFile
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("lines")] public Dictionary<string, LineFile>? Lines { get; set; }
    }

    private sealed class LineFile
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("inactive")] public List<Entry>? Inactive { get; set; }
        [JsonPropertyName("bubble")] public List<Entry>? Bubble { get; set; }
        [JsonPropertyName("streak")] public List<Entry>? Streak { get; set; }
        [JsonPropertyName("touch")] public Dictionary<string, List<Entry>>? Touch { get; set; }

        internal sealed class Entry
        {
            [JsonPropertyName("label")] public string? Label { get; set; }
            [JsonPropertyName("amount")] public string? Amount { get; set; }
            [JsonPropertyName("hint")] public string? Hint { get; set; }
            [JsonPropertyName("en")] public EnglishEntry? English { get; set; }
        }

        internal sealed class EnglishEntry
        {
            [JsonPropertyName("label")] public string? Label { get; set; }
            [JsonPropertyName("amount")] public string? Amount { get; set; }
            [JsonPropertyName("hint")] public string? Hint { get; set; }
        }
    }
}
