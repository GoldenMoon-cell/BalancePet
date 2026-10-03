using System.IO;
using System.Net.Http;

namespace BalancePet.Wpf.Services;

/// <summary>A document that arrived, and where it came from.</summary>
/// <param name="Url">The address that answered.</param>
/// <param name="Bytes">Its body.</param>
/// <param name="Mirrored">Whether it came from the mirror rather than the declared address.</param>
public sealed record GitHubContent(string Url, byte[] Bytes, bool Mirrored)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Bytes);
}

/// <summary>
/// Reads a file from a GitHub repository, over whichever of two addresses answers.
/// </summary>
/// <remarks>
/// GitHub serves repository content from raw.githubusercontent.com, and some networks
/// refuse that host while leaving the rest of GitHub reachable. Measured on one: the raw
/// host hung until the request timed out, while api.github.com, the website, and a public
/// mirror of the same repository all answered in about a second. Every document this
/// program fetches by itself — the two catalogs, the served lines, and the pictures the
/// store draws — comes from that host, so on such a network the online library could only
/// ever show what it had already cached.
///
/// The mirror address is derived from the declared one rather than read from a catalog,
/// which is what keeps a catalog from aiming the program at a host of its choosing: the
/// fallback can only ever name the same repository, the same reference and the same path.
///
/// The mirror is asked for only after the declared address has had a moment to answer,
/// rather than at the same time. Asking both at once would double the requests for every
/// picture on a network where the raw host works perfectly well, and asking in turn would
/// make the mirror pay the raw host's whole timeout before it is even tried — a list of
/// twenty rows would take twenty timeouts to fill. Waiting a moment costs one request in
/// the common case and about a second in the case this exists for.
/// </remarks>
public static class GitHubContentReader
{
    /// <summary>
    /// How long the declared address has to answer before the mirror is asked as well.
    /// </summary>
    public const int HedgeDelayMs = 1200;

    /// <summary>
    /// The declared address, followed by its mirror when there is one.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? declared)
    {
        var url = (declared ?? "").Trim();
        if (url.Length == 0) return Array.Empty<string>();
        var mirror = MirrorOf(url);
        return mirror is null ? new[] { url } : new[] { url, mirror };
    }

    /// <summary>
    /// <c>raw.githubusercontent.com/OWNER/REPO/REF/PATH</c> as
    /// <c>cdn.jsdelivr.net/gh/OWNER/REPO@REF/PATH</c>, or null for anything else.
    /// </summary>
    /// <remarks>
    /// The mirror caches a branch reference for hours, so a document committed a moment
    /// ago can be served from before that commit. That is why the declared address is
    /// always tried first and why the caller is told which one answered: a catalog that
    /// looks a version behind is a mirror, not a mistake.
    /// </remarks>
    public static string? MirrorOf(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)) return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 4) return null;
        var owner = Uri.EscapeDataString(segments[0]);
        var repository = Uri.EscapeDataString(segments[1]);
        var reference = Uri.EscapeDataString(segments[2]);
        var path = string.Join('/', segments.Skip(3).Select(Uri.EscapeDataString));
        if (owner.Length == 0 || repository.Length == 0 || reference.Length == 0 || path.Length == 0) return null;
        return $"https://cdn.jsdelivr.net/gh/{owner}/{repository}@{reference}/{path}";
    }

    /// <summary>
    /// Downloads a document from the declared address, or from its mirror.
    /// </summary>
    /// <param name="maxBytes">Ceiling on the body, so a wrong address cannot fill memory.</param>
    /// <exception cref="HttpRequestException">Neither address answered with a success status.</exception>
    /// <exception cref="InvalidDataException">An answer was empty or larger than the ceiling.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public static async Task<GitHubContent> DownloadAsync(
        HttpClient http,
        string declared,
        long maxBytes,
        string accept,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var candidates = Candidates(declared);
        if (candidates.Count == 0) throw new InvalidDataException("没有可用的下载地址。");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<GitHubContent>>
        {
            AttemptAsync(http, candidates[0], mirrored: false, maxBytes, accept, userAgent, linked.Token)
        };

        if (candidates.Count > 1)
        {
            // Whatever happens next, this wait ends: either the declared address answered,
            // or the delay passed and the mirror is asked as well. The delay itself is not
            // cancellable, because a token that fires here would end the download the
            // caller asked for.
            await Task.WhenAny(pending[0], Task.Delay(HedgeDelayMs));
            // Asked for whenever the declared address has not *succeeded* — which includes
            // having already failed. A refused connection comes back in milliseconds, and
            // waiting out the rest of the delay before trying the address that works would
            // be a stall invented by the fallback rather than suffered from the network.
            var answered = pending[0].Status == TaskStatus.RanToCompletion;
            if (!answered && !cancellationToken.IsCancellationRequested)
                pending.Add(AttemptAsync(http, candidates[1], mirrored: true, maxBytes, accept, userAgent, linked.Token));
        }

        Exception? last = null;
        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            try
            {
                var content = await finished;
                // The loser is abandoned rather than awaited: on a network where one of
                // the two addresses is blackholed, waiting for it is waiting for a timeout.
                linked.Cancel();
                return content;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException
                or TaskCanceledException or OperationCanceledException or InvalidOperationException or UriFormatException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                last = error;
            }
        }

        throw last ?? new InvalidDataException("没有可用的下载地址。");
    }

    private static async Task<GitHubContent> AttemptAsync(
        HttpClient http,
        string url,
        bool mirrored,
        long maxBytes,
        string accept,
        string userAgent,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd(userAgent);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode} {url}");

        var declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength is not null && declaredLength > maxBytes) throw new InvalidDataException("内容过大。");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0) throw new InvalidDataException("内容是空的。");
        if (bytes.Length > maxBytes) throw new InvalidDataException("内容过大。");
        return new GitHubContent(url, bytes, mirrored);
    }
}
