using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

public static class BalancePresetCatalog
{
    public const string Auto = "auto";
    public const string V1Usage = "v1-usage";
    public const string NewApiToken = "new-api-token";
    /// <summary>
    /// Deliberately not "deepseek": the pet-style combo already uses that tag, and
    /// this window has several helpers that resolve an item by its tag.
    /// </summary>
    public const string DeepSeek = "deepseek-platform";
    public const string Custom = "custom";

    private const string DeepSeekEndpoint = "https://api.deepseek.com/user/balance";
    private const string DeepSeekBalancePath = "balance_infos.0.total_balance";
    /// <summary>DeepSeek bills platform balances in yuan for mainland accounts.</summary>
    private const string DeepSeekCurrency = "CNY";

    public static string NormalizeId(string? presetId) => presetId?.Trim().ToLowerInvariant() switch
    {
        Auto => Auto,
        V1Usage => V1Usage,
        NewApiToken => NewApiToken,
        DeepSeek => DeepSeek,
        _ => Custom
    };

    /// <summary>
    /// A first-party platform whose balance endpoint is fixed. It has no relay
    /// endpoints to probe and nothing to derive from a site URL, so treating it as
    /// "automatic" made the app probe /v1/usage and /api/usage/token and report
    /// "no supported balance endpoint" against an account that works fine.
    /// </summary>
    public static bool HasFixedEndpoint(string? presetId) => NormalizeId(presetId) == DeepSeek;

    public static string FixedEndpoint(string? presetId) => HasFixedEndpoint(presetId) ? DeepSeekEndpoint : "";
    public static string FixedBalancePath(string? presetId) => HasFixedEndpoint(presetId) ? DeepSeekBalancePath : "";
    public static string FixedCurrency(string? presetId) => HasFixedEndpoint(presetId) ? DeepSeekCurrency : "";

    public static bool UsesSiteUrl(string? presetId)
    {
        var id = NormalizeId(presetId);
        return id != Custom && !HasFixedEndpoint(id);
    }

    public static string DisplayName(string? presetId, string? language = null)
    {
        var english = AppLocalization.IsEnglish(language);
        return NormalizeId(presetId) switch
        {
            Auto => english ? "Automatic detection" : "自动识别",
            V1Usage => english ? "Generic /v1/usage" : "通用 /v1/usage",
            NewApiToken => english ? "New API /api/usage/token" : "New API /api/usage/token",
            DeepSeek => english ? "DeepSeek platform" : "DeepSeek 官方平台",
            _ => english ? "Custom endpoint" : "自定义接口"
        };
    }

    public static string NormalizeSiteUrl(string? value)
    {
        var text = value?.Trim() ?? "";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return text.TrimEnd('/');

        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/api/usage/token", "/v1/usage" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        var builder = new UriBuilder(uri)
        {
            Path = string.IsNullOrEmpty(path) ? "/" : path,
            Query = "",
            Fragment = ""
        };
        return builder.Uri.GetLeftPart(UriPartial.Authority) + (path.Length == 0 ? "" : path);
    }

    public static string ResolveSiteUrl(MonitorProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SiteUrl)) return NormalizeSiteUrl(profile.SiteUrl);
        return NormalizeSiteUrl(profile.Endpoint);
    }

    public static string BuildEndpoint(string? siteUrl, string? presetId)
    {
        var site = NormalizeSiteUrl(siteUrl);
        if (string.IsNullOrWhiteSpace(site)) return "";
        return NormalizeId(presetId) switch
        {
            V1Usage => $"{site}/v1/usage",
            NewApiToken => $"{site}/api/usage/token",
            _ => site
        };
    }

    public static void Apply(MonitorProfile profile, string? presetId, string? siteUrl)
    {
        profile.PresetId = NormalizeId(presetId);

        if (HasFixedEndpoint(profile.PresetId))
        {
            profile.SiteUrl = "";
            profile.Endpoint = FixedEndpoint(profile.PresetId);
            profile.AuthMode = "bearer";
            profile.HeaderName = "Authorization";
            profile.BalancePath = FixedBalancePath(profile.PresetId);
            // Written unconditionally: the value it replaces may itself be a
            // leftover placeholder, so "preserve what the user had" preserved a
            // broken value. The field stays editable afterwards.
            profile.Currency = FixedCurrency(profile.PresetId);
            return;
        }

        if (!UsesSiteUrl(profile.PresetId)) return;

        profile.SiteUrl = NormalizeSiteUrl(siteUrl);
        profile.Endpoint = BuildEndpoint(profile.SiteUrl, profile.PresetId);
        profile.AuthMode = "bearer";
        profile.HeaderName = "Authorization";
        profile.BalancePath = profile.PresetId switch
        {
            V1Usage => "balance",
            NewApiToken => "data.total_available",
            _ => "auto"
        };
        if (profile.PresetId == NewApiToken && string.IsNullOrWhiteSpace(profile.Currency))
            profile.Currency = "USD";
    }
}
