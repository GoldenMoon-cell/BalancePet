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

    public static readonly IReadOnlyList<PetStyleDefinition> All = new[]
    {
        new PetStyleDefinition("deepseek", "DeepSeek 小鲸鱼「澜汐」", "DeepSeek Whale \"Lanxi\"", "DeepSeek 小鲸鱼", "DeepSeek Whale"),
        new PetStyleDefinition("chatgpt", "ChatGPT 小白龙「霁珑」", "ChatGPT White Dragon \"Jilong\"", "ChatGPT 小白龙", "ChatGPT White Dragon"),
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
        var builtIn = Path.Combine(root, "assets", "pets", style);
        if (RequiredStateFiles.All(file => File.Exists(Path.Combine(builtIn, file)))) return builtIn;
        var extension = Extensions.GetLatestEnabled().FirstOrDefault(info => string.Equals(info.StyleId, style, StringComparison.OrdinalIgnoreCase));
        return extension is null ? builtIn : Path.Combine(extension.DirectoryPath, "assets", "pets", extension.StyleId);
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

    private static bool IsCompleteDirectory(string directory)
        => RequiredStateFiles.All(file => File.Exists(Path.Combine(directory, file)));
}
