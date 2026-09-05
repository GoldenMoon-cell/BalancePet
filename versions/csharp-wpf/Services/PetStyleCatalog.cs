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
        new PetStyleDefinition("seedence", "Seedence 小星晶「澄芽」", "Seedence Little Star Crystal \"Chengya\"", "Seedence 小星晶", "Seedence Little Star Crystal")
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
        "seedence" or "seedance" => "seedence",
        _ => "deepseek"
    };

    public static PetStyleDefinition Get(string? value)
        => All.First(definition => string.Equals(definition.Id, NormalizeId(value), StringComparison.OrdinalIgnoreCase));

    public static bool IsAvailable(string? value, string? baseDirectory = null)
    {
        var style = NormalizeId(value);
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var directory = Path.Combine(root, "assets", "pets", style);
        return RequiredStateFiles.All(file => File.Exists(Path.Combine(directory, file)));
    }
}
