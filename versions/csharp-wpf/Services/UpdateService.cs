using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BalancePet.Wpf.Services;

public sealed record UpdateRelease(string TagName, string Name, string Body, Uri DownloadUri, string? Digest);

public sealed class UpdateService(HttpClient http)
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/GoldenMoon-cell/BalancePet/releases?per_page=20";

    public async Task<UpdateRelease?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("BalancePet-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub 更新检查失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var tag = release.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || !IsNewer(tag, currentVersion)) continue;
            if (!release.TryGetProperty("assets", out var assets)) continue;

            var expectedName = $"BalancePet-{tag.TrimStart('v', 'V')}-win-x64.zip";
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
                if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                var urlText = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
                if (!Uri.TryCreate(urlText, UriKind.Absolute, out var downloadUri)) continue;
                var title = release.TryGetProperty("name", out var titleValue) ? titleValue.GetString() : null;
                var body = release.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() ?? "" : "";
                var digest = asset.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
                return new UpdateRelease(tag, title ?? tag, body, downloadUri, digest);
            }
        }

        return null;
    }

    public async Task<string> DownloadAsync(UpdateRelease release, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUri);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.UserAgent.ParseAdd("BalancePet-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 200 * 1024 * 1024)
            throw new InvalidDataException("更新包大小异常，已停止下载。");

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BalancePet-update-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(path))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(release.Digest) && release.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var downloaded = File.OpenRead(path);
                var hash = await SHA256.HashDataAsync(downloaded, cancellationToken);
                var actual = $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
                if (!string.Equals(actual, release.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新包校验失败，文件可能已损坏或被篡改。");
            }

            return path;
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }

    private static bool IsNewer(string candidate, string current)
    {
        return TryParseVersion(candidate, out var remote) && TryParseVersion(current, out var local) && remote.CompareTo(local) > 0;
    }

    private static bool TryParseVersion(string text, out ReleaseVersion version)
    {
        var match = Regex.Match(text.Trim(), @"^v?(\d+)\.(\d+)\.(\d+)(?:-([A-Za-z0-9.-]+))?", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) || !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            version = default;
            return false;
        }

        var pre = match.Groups[4].Value;
        if (string.IsNullOrWhiteSpace(pre))
        {
            version = new ReleaseVersion(major, minor, patch, true, "", 0);
            return true;
        }

        var parts = pre.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var label = parts[0];
        var number = parts.Length > 1 && int.TryParse(parts[1], out var parsedNumber) ? parsedNumber : 0;
        version = new ReleaseVersion(major, minor, patch, false, label, number);
        return true;
    }

    private readonly record struct ReleaseVersion(int Major, int Minor, int Patch, bool Stable, string Label, int Number) : IComparable<ReleaseVersion>
    {
        public int CompareTo(ReleaseVersion other)
        {
            var result = Major.CompareTo(other.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            if (Stable != other.Stable) return Stable ? 1 : -1;
            result = string.Compare(Label, other.Label, StringComparison.OrdinalIgnoreCase);
            return result != 0 ? result : Number.CompareTo(other.Number);
        }
    }
}
