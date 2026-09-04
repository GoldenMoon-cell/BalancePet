using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

public static class BalancePresetCatalog
{
    public const string Auto = "auto";
    public const string V1Usage = "v1-usage";
    public const string NewApiToken = "new-api-token";
    public const string Custom = "custom";

    public static string NormalizeId(string? presetId) => presetId?.Trim().ToLowerInvariant() switch
    {
        Auto => Auto,
        V1Usage => V1Usage,
        NewApiToken => NewApiToken,
        _ => Custom
    };

    public static bool UsesSiteUrl(string? presetId) => NormalizeId(presetId) != Custom;

    public static string DisplayName(string? presetId, string? language = null)
    {
        var english = AppLocalization.IsEnglish(language);
        return NormalizeId(presetId) switch
        {
            Auto => english ? "Automatic detection" : "自动识别",
            V1Usage => english ? "Generic /v1/usage" : "通用 /v1/usage",
            NewApiToken => english ? "New API /api/usage/token" : "New API /api/usage/token",
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
