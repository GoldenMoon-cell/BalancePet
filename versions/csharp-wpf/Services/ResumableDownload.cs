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

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadOnceAsync(http, uri, output, ceiling, accept, userAgent, cancellationToken, progress);
                    return;
                }
                catch (Exception error) when (attempt < Attempts && IsInterrupted(error, cancellationToken))
                {
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
        IProgress<double>? progress)
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
        // again rather than being appended to.
        var resuming = response.StatusCode == HttpStatusCode.PartialContent;
        if (!resuming)
        {
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
