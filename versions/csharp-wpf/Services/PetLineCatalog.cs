using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>One thing an appearance says when it is idle, clicked, or touched.</summary>
public sealed record PetLine(string Label, string Amount, string Hint);

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
    public const int CurrentSchemaVersion = 1;

    /// <summary>Anything longer than this is not a lines file, whatever it claims.</summary>
    private const long MaxBytes = 128 * 1024;

    /// <summary>Enough for a generous set; beyond it the file is not being used as intended.</summary>
    private const int MaxLinesPerCategory = 24;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Keyed by asset directory, so a package replacing another is picked up.</summary>
    private static readonly Dictionary<string, LineFile?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

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
        var file = Load(style);
        if (file is null) return Array.Empty<PetLine>();

        var entries = category.ToLowerInvariant() switch
        {
            "inactive" => file.Inactive,
            "bubble" => file.Bubble,
            "streak" => file.Streak,
            // A part with no lines of its own still falls back to the neutral set rather
            // than to another part's: "hair" copy on an arm reads as a mistake.
            "touch" => kind is not null && (file.Touch?.TryGetValue(kind, out var zoned) ?? false) ? zoned : null,
            _ => null
        };
        return ToLines(entries);
    }

    private static IReadOnlyList<PetLine> ToLines(List<LineFile.Entry>? entries)
    {
        if (entries is null || entries.Count == 0) return Array.Empty<PetLine>();
        var lines = new List<PetLine>(Math.Min(entries.Count, MaxLinesPerCategory));
        foreach (var entry in entries)
        {
            if (lines.Count >= MaxLinesPerCategory) break;
            // A line with nothing in it would render as an empty bubble, so a partially
            // filled entry is dropped rather than shown.
            if (string.IsNullOrWhiteSpace(entry.Label) && string.IsNullOrWhiteSpace(entry.Amount)) continue;
            lines.Add(new PetLine(entry.Label ?? "", entry.Amount ?? "", entry.Hint ?? ""));
        }
        return lines;
    }

    private static LineFile? Load(string? style)
    {
        string directory;
        try { directory = PetStyleCatalog.ResolveAssetDirectory(style); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { return null; }

        lock (Gate)
        {
            if (Cache.TryGetValue(directory, out var cached)) return cached;
        }

        var parsed = Read(Path.Combine(directory, FileName));
        lock (Gate) { Cache[directory] = parsed; }
        return parsed;
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
            return document is not null && document.SchemaVersion == CurrentSchemaVersion ? document : null;
        }
        catch (JsonException) { return null; }
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
        }
    }
}
