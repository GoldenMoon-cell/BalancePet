using System.IO;
using System.Text.Json;

namespace BalancePet.UsageAnalytics;

/// <summary>
/// Resolves the opaque account ids in the balance snapshot to what the user chose
/// in the host's settings.
///
/// Only <c>monitors[].id</c>, <c>name</c>, <c>endpoint</c> and <c>preset_id</c>
/// are ever materialized. The settings file also holds DPAPI-protected access
/// tokens, so this reads through a DOM and pulls exactly those properties instead
/// of deserializing the document into a model that could carry credentials.
/// </summary>
public static class AccountDirectory
{
    public const string SettingsFileName = "csharp-settings.json";

    /// <summary>
    /// The vendor hosts whose APIs publish no per-request billing log. Hosts are
    /// matched on a dot boundary so <c>api.deepseek.com.evil.example</c> does not
    /// pass as DeepSeek.
    /// </summary>
    private static readonly string[] OfficialHosts =
    [
        "api.deepseek.com",
        "api.openai.com",
        "api.anthropic.com",
        "generativelanguage.googleapis.com",
        "dashscope.aliyuncs.com",
        "open.bigmodel.cn",
        "api.moonshot.cn"
    ];

    /// <param name="Name">Display name the user gave the account.</param>
    /// <param name="Endpoint">Configured balance endpoint, used only to classify.</param>
    /// <param name="PresetId">Preset the account was created from, when known.</param>
    /// <param name="IsSubscription">User's declaration that the account is billed by subscription.</param>
    public sealed record AccountInfo(string Name, string Endpoint, string PresetId, bool IsSubscription)
    {
        /// <summary>
        /// True when the account points at a vendor's own API rather than a relay.
        ///
        /// This matters for reading an absent cost. A relay has a billing log that
        /// either matched or did not, so "no cost" there means the lookup failed. An
        /// official account has no such log at all: its only cost signal is the
        /// balance drop a task caused, so an absent cost usually means the balance
        /// simply did not move — a short task, or one that finished inside the
        /// account's refresh interval. Showing both as "unreported" tells the user
        /// nothing about which of those happened.
        /// </summary>
        public bool IsOfficial =>
            string.Equals(PresetId, "deepseek-platform", StringComparison.OrdinalIgnoreCase)
            || OfficialHosts.Any(host => MatchesHost(Endpoint, host));

        private static bool MatchesHost(string endpoint, string host)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
            var value = uri.Host;
            return value.Equals(host, StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static IReadOnlyDictionary<string, AccountInfo> ReadAccounts(string directory)
    {
        var accounts = new Dictionary<string, AccountInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(directory, SettingsFileName);
            if (!File.Exists(path)) return accounts;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("monitors", out var monitors) || monitors.ValueKind != JsonValueKind.Array) return accounts;
            foreach (var monitor in monitors.EnumerateArray())
            {
                if (monitor.ValueKind != JsonValueKind.Object) continue;
                var key = ReadString(monitor, "id", 128);
                var name = ReadString(monitor, "name", 64);
                if (key.Length == 0 || name.Length == 0) continue;
                accounts[key] = new AccountInfo(name, ReadString(monitor, "endpoint", 512), ReadString(monitor, "preset_id", 64), ReadBool(monitor, "is_subscription"));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return accounts;
    }

    /// <summary>Account names only, for callers that need nothing else.</summary>
    public static IReadOnlyDictionary<string, string> Read(string directory) =>
        ReadAccounts(directory).ToDictionary(pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase);

    private static string ReadString(JsonElement element, string property, int maxLength)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = (value.GetString() ?? "").Trim();
        return text.Length <= maxLength ? text : "";
    }

    private static bool ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>Display name for an account id, falling back to a shortened id.</summary>
    public static string Label(IReadOnlyDictionary<string, string> names, string accountId)
    {
        if (names.TryGetValue(accountId, out var name)) return name;
        return Label(accountId);
    }

    /// <summary>Shortened id, for when the account is not in the settings at all.</summary>
    public static string Label(string accountId)
    {
        // A GUID-shaped id tells a person nothing and is 32 characters wide, so
        // shorten it instead of printing the whole thing.
        return accountId.Length <= 12 ? accountId : accountId[..10] + "…";
    }
}
