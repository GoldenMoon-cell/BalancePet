using System.IO;

namespace BalancePet.Wpf.Services;

public sealed record PetStyleDefinition(
    string Id,
    string ChineseName,
    string EnglishName,
    string ChineseShortName,
    string EnglishShortName);

public static class PetStyleCatalog
{
    public static readonly IReadOnlyList<string> RequiredStateFiles = new[]
    {
        "idle.png",
        "loading.png",
        "success.png",
        "error.png",
        "low.png",
        "inactive.png",
        "clicked.png",
        "codex-working.png",
        "codex-done.png"
    };

    private static readonly PetExtensionManager Extensions = new();

    /// <summary>
    /// The id of the appearance that ships inside the application folder and is never
    /// published as a package, so there is always something to draw.
    /// </summary>
    /// <remarks>
    /// The leading underscore is deliberate and load-bearing: it fails
    /// <see cref="IsExtensionStyleId"/>, so no package can ever claim this id and
    /// displace the safety net with a download that might not arrive.
    /// </remarks>
    public const string FallbackId = "_placeholder";

    /// <summary>
    /// Appearances that remain part of the distribution.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Every appearance here adds its artwork to the installer
    /// and to every download, and the artwork is what dominates both, so the rest
    /// are published as resource-extension packages instead. This list is only
    /// "what is guaranteed to arrive with the program"; anything that needs "what
    /// can be drawn right now" wants <see cref="GetAvailableStyles"/>.
    /// </remarks>
    public static readonly IReadOnlyList<PetStyleDefinition> All = new[]
    {
        new PetStyleDefinition("deepseek", "DeepSeek 小鲸鱼「澜汐」", "DeepSeek Whale \"Lanxi\"", "DeepSeek 小鲸鱼", "DeepSeek Whale"),
        new PetStyleDefinition("chatgpt", "ChatGPT 小白龙「霁珑」", "ChatGPT White Dragon \"Jilong\"", "ChatGPT 小白龙", "ChatGPT White Dragon"),
        // Never published as artwork, and not something to choose on purpose. It is
        // the shape the window draws when no appearance is installed yet, which is a
        // real state now that appearances arrive as packages: without it a fresh
        // installation that cannot reach the network would have an empty window.
        new PetStyleDefinition(FallbackId, "内置占位形象", "Built-in Placeholder", "占位形象", "Placeholder"),
    };

    /// <summary>
    /// Appearances that are not part of the distribution: published as packages, or
    /// registered ahead of their artwork.
    /// </summary>
    /// <remarks>
    /// Kept as data rather than deleted because an upgraded installation still has
    /// these folders on disk, and the migration has to name them when it builds
    /// their packages. Membership here says nothing about availability: a fresh
    /// installation has none of these folders, and after migration the installed
    /// package supplies the appearance. Read <see cref="GetAvailableStyles"/> for
    /// that question instead.
    /// </remarks>
    public static readonly IReadOnlyList<PetStyleDefinition> Extractable = new[]
    {
        new PetStyleDefinition("minimax", "MiniMax 小海螺「绯音」", "MiniMax Shell \"Feiyin\"", "MiniMax 小海螺", "MiniMax Shell"),
        new PetStyleDefinition("gemini", "Gemini 小星猫「星璃」", "Gemini Star Cat \"Xingli\"", "Gemini 小星猫", "Gemini Star Cat"),
        new PetStyleDefinition("grok", "Grok 小恶魔「烬斧」", "Grok Little Demon \"Jinfu\"", "Grok 小恶魔", "Grok Little Demon"),
        new PetStyleDefinition("claude", "Claude 小书灵「丹笺」", "Claude Little Book Spirit \"Danqian\"", "Claude 小书灵", "Claude Little Book Spirit"),
        new PetStyleDefinition("kimi", "Kimi 小棱镜「虹谱」", "Kimi Little Prism \"Hongpu\"", "Kimi 小棱镜", "Kimi Little Prism"),
        new PetStyleDefinition("qwen", "Qwen 小折扇「绀华」", "Qwen Folding Fan \"Ganhua\"", "Qwen 小折扇", "Qwen Folding Fan"),
        new PetStyleDefinition("ernie", "Ernie 小病书灵「青绡」", "Ernie Little Book Spirit \"Qingxiao\"", "Ernie 小病书灵", "Ernie Little Book Spirit"),
        new PetStyleDefinition("glm", "GLM 小方灵「青棱」", "GLM Little Square Spirit \"Qingleng\"", "GLM 小方灵", "GLM Little Square Spirit"),
        new PetStyleDefinition("gpt-image2", "GPT Image 2 小墨龙「玄珏」", "GPT Image 2 Ink Dragon \"Xuanjue\"", "GPT Image 2 小墨龙", "GPT Image 2 Ink Dragon"),
        new PetStyleDefinition("llama", "Llama 小羊驼「绒眠」", "Llama Alpaca \"Rongmian\"", "Llama 小羊驼", "Llama Alpaca"),
        new PetStyleDefinition("mimo", "MiMo 小兔码师「橙析」", "MiMo Bunny Coder \"Chengxi\"", "MiMo 小兔码师", "MiMo Bunny Coder"),
        new PetStyleDefinition("mistral", "Mistral 小猫骑士「麦霜」", "Mistral Cat Knight \"Maishuang\"", "Mistral 小猫骑士", "Mistral Cat Knight"),
        new PetStyleDefinition("opencode", "OpenCode 小码灵「墨枢」", "OpenCode Code Sprite \"Moshu\"", "OpenCode 小码灵", "OpenCode Code Sprite"),
        new PetStyleDefinition("perplexity", "Perplexity 小探灯「青鉴」", "Perplexity Little Lantern \"Qingjian\"", "Perplexity 小探灯", "Perplexity Little Lantern"),
        new PetStyleDefinition("rwkv", "RWKV 小乌鸦「夜翎」", "RWKV Little Raven \"Yeling\"", "RWKV 小乌鸦", "RWKV Little Raven"),
        new PetStyleDefinition("seedance", "Seedance 小星晶「澄芽」", "Seedance Little Star Crystal \"Chengya\"", "Seedance 小星晶", "Seedance Little Star Crystal")
    };

    public static string NormalizeId(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "_placeholder" => FallbackId,
        "chatgpt" or "gpt" => "chatgpt",
        "minimax" => "minimax",
        "gemini" => "gemini",
        "grok" => "grok",
        "claude" => "claude",
        "kimi" => "kimi",
        "qwen" => "qwen",
        "ernie" or "wenxin" => "ernie",
        "glm" => "glm",
        "gpt-image2" or "gpt image2" or "gpt image 2" => "gpt-image2",
        "llama" => "llama",
        "mimo" => "mimo",
        "mistral" => "mistral",
        "opencode" or "open-code" => "opencode",
        "perplexity" => "perplexity",
        "rwkv" => "rwkv",
        "seedance" => "seedance",
        _ => IsExtensionStyleId(value) ? value!.Trim().ToLowerInvariant() : "deepseek"
    };

    public static PetStyleDefinition Get(string? value)
    {
        return TryGetDefinition(value, out var definition) ? definition : All[0];
    }

    /// <summary>
    /// Resolves a value only when it is an actual built-in or installed pet style.
    /// Unlike <see cref="NormalizeId"/>, this method does not turn arbitrary values
    /// into the DeepSeek fallback. That distinction is important for controls that
    /// also use tags for unrelated values (for example the language selector).
    /// </summary>
    public static bool TryGetDefinition(string? value, out PetStyleDefinition definition)
    {
        definition = All[0];
        if (string.IsNullOrWhiteSpace(value)) return false;

        var raw = value.Trim();
        var canonicalId = raw.ToLowerInvariant() switch
        {
            "_placeholder" => FallbackId,
            "deepseek" => "deepseek",
            "chatgpt" or "gpt" => "chatgpt",
            "minimax" => "minimax",
            "gemini" => "gemini",
            "grok" => "grok",
            "claude" => "claude",
            "kimi" => "kimi",
            "qwen" => "qwen",
            "ernie" or "wenxin" => "ernie",
            "glm" => "glm",
            "gpt-image2" or "gpt image2" or "gpt image 2" => "gpt-image2",
            "llama" => "llama",
            "mimo" => "mimo",
            "mistral" => "mistral",
            "opencode" or "open-code" => "opencode",
            "perplexity" => "perplexity",
            "rwkv" => "rwkv",
            "seedance" => "seedance",
            _ => null
        };

        if (canonicalId is not null)
        {
            var builtIn = All.FirstOrDefault(item => string.Equals(item.Id, canonicalId, StringComparison.OrdinalIgnoreCase));
            if (builtIn is not null)
            {
                definition = builtIn;
                return true;
            }

            // An appearance that is no longer distributed still has to resolve. A
            // saved setting stores its id, and an upgraded installation still has the
            // folder on disk until the migration converts it, so returning false here
            // would drop those users onto the default appearance.
            var extractable = Extractable.FirstOrDefault(item => string.Equals(item.Id, canonicalId, StringComparison.OrdinalIgnoreCase));
            if (extractable is not null)
            {
                definition = extractable;
                return true;
            }
        }

        if (!IsExtensionStyleId(raw)) return false;
        var extension = Extensions.GetLatestEnabled().FirstOrDefault(info => string.Equals(info.StyleId, raw, StringComparison.OrdinalIgnoreCase));
        if (extension is null) return false;
        var nameEn = string.IsNullOrWhiteSpace(extension.Manifest.NameEn) ? extension.Manifest.Name : extension.Manifest.NameEn;
        definition = new PetStyleDefinition(extension.StyleId, extension.Manifest.Name, nameEn, extension.Manifest.Name, nameEn);
        return true;
    }

    public static string ResolveAssetDirectory(string? value, string? baseDirectory = null)
    {
        var style = NormalizeId(value);
        var root = baseDirectory ?? AppContext.BaseDirectory;

        // The installed package is checked first, and that order matters. Repairing or
        // reinstalling the application restores the shipped folders, and a leftover
        // folder would otherwise win over the package the user installed or updated —
        // so a redrawn appearance would keep drawing the old artwork, which is the one
        // outcome the whole package system exists to avoid.
        var extension = Extensions.GetLatestEnabled().FirstOrDefault(info => string.Equals(info.StyleId, style, StringComparison.OrdinalIgnoreCase));
        if (extension is not null)
        {
            var packaged = Path.Combine(extension.DirectoryPath, "assets", "pets", extension.StyleId);
            if (IsCompleteDirectory(packaged)) return packaged;
        }

        return Path.Combine(root, "assets", "pets", style);
    }

    public static IReadOnlyList<PetStyleDefinition> GetAvailableExtensionStyles()
        => Extensions.GetLatestEnabled()
            .Where(info => RequiredStateFiles.All(file => File.Exists(Path.Combine(info.DirectoryPath, "assets", "pets", info.StyleId, file))))
            .Select(info => new PetStyleDefinition(info.StyleId, info.Manifest.Name,
                string.IsNullOrWhiteSpace(info.Manifest.NameEn) ? info.Manifest.Name : info.Manifest.NameEn,
                info.Manifest.Name, string.IsNullOrWhiteSpace(info.Manifest.NameEn) ? info.Manifest.Name : info.Manifest.NameEn))
            .ToArray();

    public static bool IsAvailable(string? value, string? baseDirectory = null)
    {
        var style = NormalizeId(value);
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var directory = Path.Combine(root, "assets", "pets", style);
        if (RequiredStateFiles.All(file => File.Exists(Path.Combine(directory, file)))) return true;
        return Extensions.GetLatestEnabled().Any(info => string.Equals(info.StyleId, style, StringComparison.OrdinalIgnoreCase)
            && RequiredStateFiles.All(file => File.Exists(Path.Combine(info.DirectoryPath, "assets", "pets", info.StyleId, file))));
    }

    private static bool IsExtensionStyleId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 2 and <= 64
            && value.Trim().All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '.' or '-')
            && char.IsLetterOrDigit(value.Trim()[0]) && char.IsLetterOrDigit(value.Trim()[^1]);

    /// <summary>
    /// Every appearance the app can draw right now, shipped and installed alike.
    /// </summary>
    /// <remarks>
    /// A shipped pet is only a package that happens to live inside the application
    /// folder, so listing both here is what lets an appearance move out of the
    /// distribution and become installable without any caller noticing. Callers that
    /// need to know which is which should ask <see cref="IsShipped"/> rather than
    /// keeping their own list of ids, because that list is exactly what changes when
    /// a pet is extracted.
    /// </remarks>
    public static IReadOnlyList<PetStyleDefinition> GetAvailableStyles()
    {
        var styles = new Dictionary<string, PetStyleDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in All)
        {
            if (IsCompleteDirectory(Path.Combine(AppContext.BaseDirectory, "assets", "pets", definition.Id)))
                styles[definition.Id] = definition;
        }
        // Appearances that are no longer distributed still count while their folder
        // is on disk, which is the whole window between an upgrade and the migration
        // converting them. Without this the selector would empty out on first launch
        // after upgrading and the user would appear to have lost their pet.
        foreach (var definition in Extractable)
        {
            if (IsCompleteDirectory(Path.Combine(AppContext.BaseDirectory, "assets", "pets", definition.Id)))
                styles[definition.Id] = definition;
        }
        // An installed package of the same id deliberately wins: it is the newer
        // copy, and it is the one the user can see and manage.
        foreach (var definition in GetAvailableExtensionStyles()) styles[definition.Id] = definition;
        return styles.Values.ToArray();
    }

    /// <summary>True when the appearance is a package shipped inside the app folder.</summary>
    public static bool IsShipped(string? value)
    {
        var style = NormalizeId(value);
        return All.Any(definition => string.Equals(definition.Id, style, StringComparison.OrdinalIgnoreCase))
            && IsCompleteDirectory(Path.Combine(AppContext.BaseDirectory, "assets", "pets", style));
    }

    /// <summary>
    /// Whether removing this appearance would leave the app with nothing to draw.
    /// </summary>
    /// <remarks>
    /// The pet is the entire window. With no appearance left the window would be
    /// blank and the shape selector would offer nothing, so there would be no way
    /// back through the interface. The last one is therefore not removable; the
    /// caller is expected to explain that rather than let the removal fail silently.
    /// </remarks>
    public static bool IsLastAvailableStyle(string? value)
    {
        var available = GetAvailableStyles();
        if (available.Count > 1) return false;
        var style = NormalizeId(value);
        return available.Any(definition => string.Equals(definition.Id, style, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Upper bound on the frames a state may publish. Beyond this the loop would be
    /// longer than the eye can follow and the frames would cost more to decode than
    /// the movement is worth.
    /// </summary>
    public const int MaxAnimationFrames = 8;

    /// <summary>
    /// The frames for one state, in playback order.
    /// </summary>
    /// <remarks>
    /// The first frame keeps the plain name — <c>idle.png</c> — and any further frames
    /// add a suffix: <c>idle-2.png</c>, <c>idle-3.png</c>. That ordering is what makes
    /// animation additive rather than a new contract: an appearance with a single frame
    /// per state is exactly an appearance with no extra frames, and a host that knows
    /// nothing about cycling still loads a correct still image from the same path it
    /// always used.
    ///
    /// Numbering starts at two rather than one because <c>idle-1.png</c> would be a
    /// second spelling of <c>idle.png</c>, and one file with two names is a way to get
    /// the two out of step. A gap ends the sequence: <c>idle-2</c> missing means
    /// <c>idle-3</c> is not read, so a partially published set plays what it has
    /// instead of skipping.
    /// </remarks>
    public static IReadOnlyList<string> ResolveStateFrames(string? value, string stateName, string? baseDirectory = null)
    {
        var directory = ResolveAssetDirectory(value, baseDirectory);
        var first = Path.Combine(directory, $"{stateName}.png");
        if (!File.Exists(first)) return Array.Empty<string>();

        var frames = new List<string> { first };
        for (var index = 2; index <= MaxAnimationFrames; index++)
        {
            var next = Path.Combine(directory, $"{stateName}-{index}.png");
            if (!File.Exists(next)) break;
            frames.Add(next);
        }
        return frames;
    }

    private static bool IsCompleteDirectory(string directory)
        => RequiredStateFiles.All(file => File.Exists(Path.Combine(directory, file)));
}
