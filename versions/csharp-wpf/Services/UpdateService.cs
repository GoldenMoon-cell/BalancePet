using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BalancePet.Wpf.Services;

public enum UpdateAssetKind
{
    PortableArchive,
    Installer
}

public sealed record UpdateAsset(UpdateAssetKind Kind, string Name, Uri DownloadUri, string? Digest);

public sealed record UpdateRelease(
    string TagName,
    string Name,
    string Body,
    UpdateAsset? PortableArchive,
    UpdateAsset? Installer);

public sealed class UpdateService(HttpClient http)
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/GoldenMoon-cell/BalancePet/releases?per_page=20";

    public async Task<UpdateRelease?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(new Uri(ReleasesEndpoint), "更新检查", cancellationToken);
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var tag = release.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || !IsNewer(tag, currentVersion)) continue;
            var assets = await LoadCurrentAssetsAsync(release, cancellationToken);
            if (assets.Count == 0) continue;

            var version = tag.TrimStart('v', 'V');
            UpdateAsset? archive = null;
            UpdateAsset? installer = null;
            foreach (var asset in assets)
            {
                var name = asset.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
                var urlText = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
                if (!Uri.TryCreate(urlText, UriKind.Absolute, out var downloadUri)) continue;
                var digest = asset.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
                if (Matches(name, $"BalancePet-{tag}-win-x64.zip", $"BalancePet-{version}-win-x64.zip"))
                    archive = new UpdateAsset(UpdateAssetKind.PortableArchive, name!, downloadUri, digest);
                else if (Matches(name, $"BalancePet-{tag}-Setup.exe", $"BalancePet-{version}-Setup.exe"))
                    installer = new UpdateAsset(UpdateAssetKind.Installer, name!, downloadUri, digest);
            }

            if (archive is null && installer is null) continue;
            var title = release.TryGetProperty("name", out var titleValue) ? titleValue.GetString() : null;
            var body = release.TryGetProperty("body", out var bodyValue) ? bodyValue.GetString() ?? "" : "";
            return new UpdateRelease(tag, title ?? tag, body, archive, installer);
        }

        return null;
    }

    private async Task<IReadOnlyList<JsonElement>> LoadCurrentAssetsAsync(JsonElement release, CancellationToken cancellationToken)
    {
        if (!release.TryGetProperty("assets_url", out var assetsUrlValue) ||
            !Uri.TryCreate(assetsUrlValue.GetString(), UriKind.Absolute, out var assetsUri) ||
            assetsUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(assetsUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<JsonElement>();

        var builder = new UriBuilder(assetsUri);
        builder.Query = "per_page=100";
        using var document = await GetJsonAsync(builder.Uri, "更新资产读取", cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<JsonElement>();
        return document.RootElement.EnumerateArray().Select(asset => asset.Clone()).ToArray();
    }

    /// <summary>
    /// How many times a request is sent before its failure is reported.
    /// </summary>
    private const int RequestAttempts = 3;

    /// <summary>
    /// Sends a GET, parses the JSON body, and retries a connection that dropped.
    /// </summary>
    /// <remarks>
    /// A dropped TLS connection arrives as an IOException, or as an
    /// HttpRequestException carrying no status code, and at this level it looks
    /// exactly like a server that is briefly unreachable. Both are the absence of an
    /// answer rather than an answer, and that is the only case where sending the same
    /// request again can change the outcome: an HTTP status is the server having
    /// replied, and repeating the request gets the same reply.
    ///
    /// Without this a single dropped packet during the handshake ends the check, and
    /// the user gets an error dialog about something they cannot act on. Asking again
    /// costs nothing while the network is healthy.
    /// </remarks>
    private async Task<JsonDocument> GetJsonAsync(Uri endpoint, string what, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // Rebuilt per attempt, because a request message cannot be sent twice.
                    using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    request.Headers.UserAgent.ParseAdd("BalancePet-Updater/1.0");
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException(
                            $"GitHub {what}失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                            inner: null,
                            statusCode: response.StatusCode);

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }
                catch (Exception error) when (attempt < RequestAttempts && IsDroppedConnection(error, cancellationToken))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                }
            }
        }
        catch (Exception error) when (IsDroppedConnection(error, cancellationToken))
        {
            // Out of attempts and still no answer. The message deliberately does not
            // name the operation: every caller already says which one failed, and
            // repeating it turns the dialog into "检查更新失败：更新检查时…". The
            // transport's own words are kept as a suffix, because they are what makes
            // a report diagnosable when "网络问题" is not the real cause.
            throw new HttpRequestException($"网络连接中断，请稍后重试。（{error.Message}）", error);
        }
    }

    /// <summary>
    /// Whether a failure is a connection that dropped rather than a reply.
    /// </summary>
    private static bool IsDroppedConnection(Exception error, CancellationToken cancellationToken)
    {
        // A cancellation is the user's own doing, and a malformed body is an answer
        // that arrived; neither improves by asking again. A timeout is excluded for a
        // different reason: the request timeout is measured in minutes, so retrying it
        // would leave the user waiting for a second one before being told anything.
        if (cancellationToken.IsCancellationRequested) return false;
        return error switch
        {
            OperationCanceledException or JsonException => false,
            HttpRequestException http => http.StatusCode is null,
            IOException => true,
            System.Net.Sockets.SocketException => true,
            _ => false
        };
    }

    public async Task<string> DownloadAsync(UpdateAsset asset, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        var extension = asset.Kind == UpdateAssetKind.Installer ? ".exe" : ".zip";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"BalancePet-update-{Guid.NewGuid():N}{extension}");
        try
        {
            await DownloadToFileAsync(asset, path, cancellationToken, progress);

            if (!string.IsNullOrWhiteSpace(asset.Digest) && asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using var downloaded = File.OpenRead(path);
                var hash = await SHA256.HashDataAsync(downloaded, cancellationToken);
                var actual = $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
                if (!string.Equals(actual, asset.Digest, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// How many times a download is attempted before its failure is reported.
    /// </summary>
    /// <remarks>
    /// Higher than the JSON requests get, because the work is resumable: an attempt
    /// that stops part way still leaves the bytes it received, so another one costs a
    /// wait rather than the whole transfer.
    /// </remarks>
    private const int DownloadAttempts = 6;

    /// <summary>
    /// Downloads the asset to a file, resuming a transfer that was cut short.
    /// </summary>
    private async Task DownloadToFileAsync(UpdateAsset asset, string path, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadOnceAsync(asset, path, cancellationToken, progress);
                    return;
                }
                catch (Exception error) when (attempt < DownloadAttempts && IsInterruptedDownload(error, cancellationToken))
                {
                    // The partial file is deliberately left in place: the next attempt
                    // asks for the remainder instead of starting over. On a connection
                    // that drops, restarting is what turns a slow download into one
                    // that never finishes.
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                }
            }
        }
        catch (Exception error) when (IsInterruptedDownload(error, cancellationToken))
        {
            throw new HttpRequestException($"下载更新包时网络连接中断，请稍后重试。（{error.Message}）", error);
        }
    }

    /// <summary>One ranged request, appended to whatever is already on disk.</summary>
    private async Task DownloadOnceAsync(UpdateAsset asset, string path, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        var have = File.Exists(path) ? new FileInfo(path).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.UserAgent.ParseAdd("BalancePet-Updater/1.0");
        if (have > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Asked for the remainder of a file that is already complete. Nothing to do;
        // the caller's digest check is what decides whether it really is complete.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            progress?.Report(1);
            return;
        }
        response.EnsureSuccessStatusCode();

        // A server that ignores Range answers 200 with the whole file. That is still a
        // correct answer, so the part on disk is discarded and the transfer restarts
        // rather than being appended to.
        var resuming = response.StatusCode == HttpStatusCode.PartialContent;
        if (!resuming) have = 0;

        var received = response.Content.Headers.ContentLength ?? -1;
        var total = resuming ? have + received : received;
        if (total > 512L * 1024 * 1024)
            throw new InvalidDataException("更新包大小异常，已停止下载。");

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(path, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        {
            // A manual copy loop rather than CopyToAsync: the update package is
            // hundreds of megabytes, and with no running count the pet has
            // nothing to show for the whole download.
            var buffer = new byte[81920];
            var copied = have;
            var lastPercent = -1;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (progress is null || total <= 0) continue;
                var percent = (int)Math.Clamp(copied * 100 / total, 0, 100);
                // Whole percents only: every report is a UI update, and 80 KB
                // chunks would otherwise flood the dispatcher.
                if (percent == lastPercent) continue;
                lastPercent = percent;
                progress.Report(percent / 100d);
            }
        }
        progress?.Report(1);
    }

    /// <summary>
    /// Whether a download should be attempted again.
    /// </summary>
    /// <remarks>
    /// This is <see cref="IsDroppedConnection"/> plus the request timeout. A timeout is
    /// worth another attempt here and not for the JSON requests, precisely because the
    /// bytes already on disk are kept: the second attempt continues the transfer
    /// instead of paying for it twice. A cancellation is still the user's own doing and
    /// is never retried.
    /// </remarks>
    private static bool IsInterruptedDownload(Exception error, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && (error is OperationCanceledException || IsDroppedConnection(error, CancellationToken.None));

    private static bool Matches(string? name, params string[] expectedNames) =>
        !string.IsNullOrWhiteSpace(name) && expectedNames.Contains(name, StringComparer.OrdinalIgnoreCase);

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
