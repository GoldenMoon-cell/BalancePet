using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

public sealed class ExtensionUpdateRelease
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("package_name")] public string PackageName { get; set; } = "";
    [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("digest")] public string Digest { get; set; } = "";
    [JsonPropertyName("checked_at_utc")] public DateTimeOffset CheckedAtUtc { get; set; }
}

public sealed record ExtensionUpdateCheckResult(
    IReadOnlyDictionary<string, ExtensionUpdateRelease> Releases,
    int CheckedCount,
    IReadOnlyList<string> Errors);

/// <summary>
/// Checks extension-owned GitHub Releases without sending settings, tokens, or
/// usage data. Downloaded ZIPs are still validated by the normal installer.
/// </summary>
public sealed class ExtensionUpdateService(HttpClient http)
{
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "extension-updates.json");

    public IReadOnlyDictionary<string, ExtensionUpdateRelease> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return new Dictionary<string, ExtensionUpdateRelease>(StringComparer.OrdinalIgnoreCase);
            var values = JsonSerializer.Deserialize<List<ExtensionUpdateRelease>>(File.ReadAllText(CachePath), JsonOptions) ?? new();
            return values.Where(IsValidCachedRelease)
                .GroupBy(value => Key(value.Type, value.Id), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(value => ParseVersion(value.Version)).First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, ExtensionUpdateRelease>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<ExtensionUpdateCheckResult> CheckAsync(
        IEnumerable<ExtensionCatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var cache = LoadCache().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var targets = entries.Where(entry => entry.IsInstalled && !string.IsNullOrWhiteSpace(entry.UpdateUrl))
            .GroupBy(entry => Key(entry.Type, entry.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        var errors = new List<string>();

        foreach (var entry in targets)
        {
            try
            {
                var release = await CheckOneAsync(entry, cancellationToken);
                if (release is not null) cache[Key(entry.Type, entry.Id)] = release;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
            {
                errors.Add($"{entry.Id}: {error.Message}");
            }
        }

        SaveCache(cache.Values);
        return new ExtensionUpdateCheckResult(cache, targets.Length, errors);
    }

    public static void ApplyCachedUpdates(IEnumerable<ExtensionCatalogEntry> entries, IReadOnlyDictionary<string, ExtensionUpdateRelease> cache)
    {
        foreach (var entry in entries)
            if (cache.TryGetValue(Key(entry.Type, entry.Id), out var release)) entry.RemoteUpdate = release;
    }

    public async Task<string> DownloadAsync(ExtensionUpdateRelease release, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("扩展更新下载地址无效。");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.UserAgent.ParseAdd("BalancePet-Extension-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is <= 0 or > MaxPackageBytes)
            throw new InvalidDataException("扩展更新包大小异常。");

        var path = Path.Combine(Path.GetTempPath(), $"BalancePet-extension-update-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(path))
                await input.CopyToAsync(output, cancellationToken);
            if (new FileInfo(path).Length is <= 0 or > MaxPackageBytes)
                throw new InvalidDataException("扩展更新包大小异常。");
            if (!string.IsNullOrWhiteSpace(release.Digest) && release.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var downloaded = File.OpenRead(path);
                var hash = await SHA256.HashDataAsync(downloaded, cancellationToken);
                var actual = $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
                if (!string.Equals(actual, release.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("扩展更新包校验失败，文件可能已损坏或被篡改。");
            }
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    public static string Key(string type, string id) => $"{type}:{id}";

    private async Task<ExtensionUpdateRelease?> CheckOneAsync(ExtensionCatalogEntry entry, CancellationToken cancellationToken)
    {
        if (!TryValidateGitHubReleaseUrl(entry.UpdateUrl, out var endpoint))
            throw new InvalidDataException("update_url 必须是 api.github.com 的 releases/latest 地址。");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("BalancePet-Extension-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        var tag = ReadString(root, "tag_name");
        var version = tag.TrimStart('v', 'V');
        if (ParseVersion(version) <= new Version(0, 0, 0)) throw new InvalidDataException("GitHub Release 版本号无效。");
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = ReadString(asset, "name");
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !name.Contains(version, StringComparison.OrdinalIgnoreCase)) continue;
            var url = ReadString(asset, "browser_download_url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var download) || download.Scheme != Uri.UriSchemeHttps) continue;
            return new ExtensionUpdateRelease
            {
                Id = entry.Id,
                Type = entry.Type,
                Version = version,
                PackageName = name,
                DownloadUrl = url,
                Digest = ReadString(asset, "digest"),
                CheckedAtUtc = DateTimeOffset.UtcNow
            };
        }
        return null;
    }

    private static bool TryValidateGitHubReleaseUrl(string value, out Uri endpoint)
    {
        endpoint = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 5 || !parts[0].Equals("repos", StringComparison.OrdinalIgnoreCase) ||
            !parts[3].Equals("releases", StringComparison.OrdinalIgnoreCase) ||
            !parts[4].Equals("latest", StringComparison.OrdinalIgnoreCase)) return false;
        endpoint = uri;
        return true;
    }

    private static void SaveCache(IEnumerable<ExtensionUpdateRelease> releases)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var temporary = CachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(releases.OrderBy(value => value.Type).ThenBy(value => value.Id).ToArray(), JsonOptions));
            File.Move(temporary, CachePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsValidCachedRelease(ExtensionUpdateRelease value)
        => !string.IsNullOrWhiteSpace(value.Id) && value.Type is "pet" or "feature" or "theme" &&
           ParseVersion(value.Version) > new Version(0, 0, 0) &&
           Uri.TryCreate(value.DownloadUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";

    private static Version ParseVersion(string value)
    {
        var numeric = value?.Split('-', '+')[0];
        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0, 0);
    }
}
