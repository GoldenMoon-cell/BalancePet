using System.IO;
using System.Text.Json;

namespace BalancePet.UsageAnalytics;

/// <summary>
/// Resolves the opaque account ids in the balance snapshot to the names the user
/// chose in the host's settings.
///
/// Only <c>monitors[].id</c> and <c>monitors[].name</c> are ever materialized.
/// The settings file also holds DPAPI-protected access tokens, so this reads
/// through a DOM and pulls exactly those two string properties instead of
/// deserializing the document into a model that could carry credentials.
/// </summary>
public static class AccountDirectory
{
    public const string SettingsFileName = "csharp-settings.json";

    public static IReadOnlyDictionary<string, string> Read(string directory)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(directory, SettingsFileName);
            if (!File.Exists(path)) return names;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("monitors", out var monitors) || monitors.ValueKind != JsonValueKind.Array) return names;
            foreach (var monitor in monitors.EnumerateArray())
            {
                if (monitor.ValueKind != JsonValueKind.Object) continue;
                if (!monitor.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                if (!monitor.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
                var key = id.GetString();
                var value = name.GetString();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;
                if (key.Length > 128 || value.Length > 64) continue;
                names[key] = value.Trim();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return names;
    }

    /// <summary>Display name for an account id, falling back to a shortened id.</summary>
    public static string Label(IReadOnlyDictionary<string, string> names, string accountId)
    {
        if (names.TryGetValue(accountId, out var name)) return name;
        // A GUID-shaped id tells a person nothing and is 32 characters wide, so
        // shorten it instead of printing the whole thing.
        return accountId.Length <= 12 ? accountId : accountId[..10] + "…";
    }
}
