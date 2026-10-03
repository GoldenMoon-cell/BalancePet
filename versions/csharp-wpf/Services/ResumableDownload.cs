using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Downloads a file that may take several connections to arrive.
/// </summary>
/// <remarks>
/// Written because of what this network does to a long transfer. Measured here by
/// fetching a twelve megabyte release asset: with HTTP/1.1 one attempt in four
/// completed, with HTTP/2 two in four, and every failure was the same shape — a
/// connection cut roughly three seconds in, on both versions, and on three different
/// public mirrors at the same three-second mark. The same file over the same link
/// finished in 2.1 seconds on one attempt and 46 on another.
///
/// A connection like that does not need a better protocol so much as it needs the
/// transfer to be resumable. Restarting is what turns a slow download into one that
/// never finishes: at 278 KB/s a seventy megabyte update is four minutes, and a drop at
/// minute three that starts over has cost three minutes for nothing. So the bytes that
/// arrived are kept, the next attempt asks for the remainder, and the caller's digest
/// check is what decides whether the result is the file it asked for.
///
/// This is the whole reason the update downloader worked on such a link while extension
/// downloads did not: one kept its partial file and the other started again.
/// </remarks>
public static class ResumableDownload
{
    /// <summary>
    /// How many connections a single download may take before the failure is reported.
    /// </summary>
    /// <remarks>
    /// Generous, because an attempt that stops part way still leaves what it received:
    /// another attempt costs a wait rather than the whole transfer.
    /// </remarks>
    public const int Attempts = 6;

    /// <summary>The largest file this will agree to fetch, whatever the caller expects.</summary>
    public const long AbsoluteMaxBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Where a download waits while it is being fetched.
    /// </summary>
    /// <remarks>
    /// Not the temp directory. A transfer that needs several attempts has to survive the
    /// program being closed, and a system that cleans the temp directory decides for
    /// itself when that happens — a seventy megabyte update that is four fifths complete
    /// is not something to lose to a disk cleanup. Beside the program's own data it is
    /// also the directory the user can find and delete.
    /// </remarks>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "downloads");

    /// <summary>
    /// The path one artifact is fetched to, derived from its name so that it is the same
    /// path every time.
    /// </summary>
    /// <remarks>
    /// Stable on purpose. A name that changes per attempt is a name that cannot be
    /// resumed, and on a link that drops every few seconds a seventy megabyte file needs
    /// more than one attempt to arrive — so the user pressing the button again, or
    /// starting the program tomorrow, continues from what is already there instead of
    /// beginning again.
    /// </remarks>
    public static string PathFor(string directory, string? name, string fallbackName)
    {
        var candidate = (name ?? "").Trim();
        if (candidate.Length == 0) candidate = fallbackName;
        var safe = new string(candidate
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ' ')
            .ToArray())
            .Trim();
        if (safe.Length == 0) safe = fallbackName;
        return Path.Combine(directory, safe);
    }

    /// <summary>
    /// Removes what an earlier download left behind and this one is not using.
    /// </summary>
    /// <remarks>
    /// Kept for a day so that a download in progress is not deleted by another one that
    /// happens to finish first: the update check and a skin install are separate actions
    /// and can overlap. Beyond that a partial file is abandoned work, and the alternative
    /// is a directory that only grows — a version that was superseded halfway through
    /// would otherwise sit there for the life of the installation.
    /// </remarks>
    public static void PruneStale(string directory, string keep, TimeSpan? olderThan = null)
    {
        var age = olderThan ?? TimeSpan.FromDays(1);
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(path, keep, StringComparison.OrdinalIgnoreCase)) continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < age) continue;
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Fetches <paramref name="uri"/> into <paramref name="path"/>, resuming as needed.
    /// </summary>
    /// <param name="maxBytes">Ceiling on the finished file; a larger answer is refused.</param>
    /// <param name="accept">What the caller wants, for hosts that vary their answer.</param>
    /// <param name="userAgent">Who is asking; GitHub refuses a request without one.</param>
    /// <param name="progress">Whole-percent updates, or null.</param>
    /// <exception cref="HttpRequestException">The connection kept dropping, or the host refused.</exception>
    /// <exception cref="InvalidDataException">The answer was larger than expected.</exception>
    public static async Task DownloadAsync(
        HttpClient http,
        Uri uri,
        string path,
        long maxBytes,
        string accept,
        string userAgent,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        var ceiling = Math.Min(maxBytes, AbsoluteMaxBytes);
        // Created rather than assumed: a download directory is one of the things a fresh
        // installation does not have, and the first update anybody runs is the one that
        // would have discovered it.
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            try { Directory.CreateDirectory(folder); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // One handle for the whole download rather than reopening the file for each
        // attempt. Not because reopening failed — the updater reopened it per attempt for
        // two releases and the resume test passes — but because there is no reason to ask
        // for a file twice when it is being written to for minutes at a time, and
        // FileShare.Read lets a virus scanner read what is being written without stopping
        // the transfer.
        try
        {
            await using var output = new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

            // A host that answers a range request with the whole file cannot continue a
            // transfer, and another attempt against it starts from nothing. Measured: the
            // domestic mirror does exactly this — the published address redirects to a CDN
            // that ignores Range and returns all seventy megabytes. Six attempts there
            // would be six copies of the same file over a link that drops, so the first
            // time it happens the attempts stop and the caller moves to its next address.
            var state = new AttemptState();

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadOnceAsync(http, uri, output, ceiling, accept, userAgent, cancellationToken, progress, state);
                    return;
                }
                catch (Exception error) when (attempt < Attempts && IsInterrupted(error, cancellationToken))
                {
                    if (!state.CanResume) throw;
                    // The bytes already written are left where they are: the next attempt
                    // asks the host for the remainder instead of starting over.
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                }
            }
        }
        catch (Exception error) when (IsInterrupted(error, cancellationToken))
        {
            throw new HttpRequestException($"下载时网络连接中断，请稍后重试。（{error.Message}）", error);
        }
    }

    /// <summary>What one download learns about the host while it is talking to it.</summary>
    private sealed class AttemptState
    {
        /// <summary>Whether the host honoured a range request. Set once, never unset.</summary>
        public bool CanResume { get; set; } = true;
    }

    /// <summary>
    /// Whether a failure is a connection that dropped rather than an answer.
    /// </summary>
    /// <remarks>
    /// A timeout counts here and not for a short request, precisely because the bytes
    /// already on disk are kept: another attempt continues the transfer rather than
    /// paying for it twice. A cancellation is the user's own doing and never retried;
    /// an HTTP status is an answer, and asking again gets the same answer.
    /// </remarks>
    public static bool IsInterrupted(Exception error, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        return error switch
        {
            OperationCanceledException => true,
            HttpRequestException http => http.StatusCode is null,
            IOException => true,
            System.Net.Sockets.SocketException => true,
            _ => false
        };
    }

    /// <summary>One connection's worth, appended to whatever is already in the file.</summary>
    private static async Task DownloadOnceAsync(
        HttpClient http,
        Uri uri,
        FileStream output,
        long maxBytes,
        string accept,
        string userAgent,
        CancellationToken cancellationToken,
        IProgress<double>? progress,
        AttemptState state)
    {
        var have = output.Length;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd(userAgent);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Asked for the remainder of a file that is already whole. Nothing left to do;
        // whether it really is whole is the caller's digest check to make.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            progress?.Report(1);
            return;
        }
        response.EnsureSuccessStatusCode();

        // A host that ignores Range answers 200 with the whole file. That is still a
        // correct answer, so what is in the file is discarded and the transfer starts
        // again rather than being appended to — and the host is remembered as one that
        // cannot be resumed from.
        var resuming = response.StatusCode == HttpStatusCode.PartialContent;
        if (!resuming)
        {
            if (have > 0) state.CanResume = false;
            output.SetLength(0);
            have = 0;
        }
        output.Seek(0, SeekOrigin.End);

        var received = response.Content.Headers.ContentLength ?? -1;
        var total = resuming ? have + received : received;
        if (total > maxBytes) throw new InvalidDataException("下载内容超过大小限制。");

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            // A manual loop rather than CopyToAsync, because a download without a running
            // count leaves the caller with nothing to show for minutes of waiting.
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
                // Whole percents only: every report is a UI update, and 80 KB chunks
                // would otherwise flood the dispatcher.
                if (percent == lastPercent) continue;
                lastPercent = percent;
                progress.Report(percent / 100d);
            }
        }

        // Flushed at the end of each connection rather than only when the file is closed:
        // the length of the file is what the next attempt asks from, and it is also what
        // the caller measures. A connection that drops mid-write leaves a partial buffer
        // unflushed, which is correct: those bytes did not arrive as far as anyone knows.
        await output.FlushAsync(cancellationToken);
        progress?.Report(1);
    }
}
