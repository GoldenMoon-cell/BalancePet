namespace BalancePet.Wpf.Services;

/// <summary>
/// Classifies login metadata without reading client credential stores.
/// Explicit client metadata remains useful when no endpoint is available.
/// When an endpoint is present, only known first-party hosts are treated as
/// official so a relay cannot be presented as an official API by mistake.
/// </summary>
public static class AccountSourceClassifier
{
    private static readonly HashSet<string> OfficialApiHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.openai.com",
        "api.anthropic.com",
        "generativelanguage.googleapis.com",
        "aiplatform.googleapis.com",
        "api.deepseek.com",
        "api.x.ai",
        "api.mistral.ai",
        "api.moonshot.cn",
        "dashscope.aliyuncs.com",
        "api.minimax.chat",
        "api.minimaxi.com",
        "api.perplexity.ai",
        "qianfan.baidubce.com"
    };

    public static string ResolveAccountType(AiAccountActivity activity)
    {
        if (activity.AccountType == "official") return "official";
        if (!string.IsNullOrWhiteSpace(activity.Endpoint))
        {
            if (IsKnownOfficialApi(activity.Endpoint)) return "official-api";
            return activity.AccountType == "relay-api" ? "relay-api" : "third-party";
        }
        if (activity.AccountType == "official-api") return "official-api";
        if (activity.AccountType is "relay-api" or "third-party") return activity.AccountType;
        return "unknown";
    }

    public static bool IsKnownOfficialApi(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;
        return OfficialApiHosts.Contains(uri.IdnHost);
    }
}
