using System.IO;
using System.Net;
using System.Net.Http;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Builds the HTTP clients this program uses, so the choices in them are made once.
/// </summary>
/// <remarks>
/// The version policy is why this exists. Measured on one network, downloading the same
/// twelve megabyte release asset repeatedly:
///
/// <list type="bullet">
/// <item>HTTP/1.1 answered 1 time in 4, and the one that finished took 26 seconds.</item>
/// <item>HTTP/2 answered 2 times in 4, finishing in 2.1 and 9.2 seconds — and one of the
/// downloads reached 5.9 MB/s.</item>
/// <item>Every failure was the same shape: a connection cut about three seconds in,
/// whichever version was in use.</item>
/// </list>
///
/// So this is a preference and not a cure. The link drops large transfers on its own
/// schedule, and what survives that is retrying and resuming rather than choosing a
/// protocol — the updater already keeps what it received and asks for the rest. What
/// HTTP/2 adds on top is speed when the transfer does go through, and one connection
/// carrying several answers at once, which is what the appearance list wants when it
/// asks for twenty pictures.
///
/// <see cref="HttpVersionPolicy.RequestVersionOrLower"/> rather than
/// <c>RequestVersionOrHigher</c> because the other end of this is not always GitHub: the
/// balance endpoint is whatever the user typed, and a server that speaks only HTTP/1.1
/// has to keep working. This asks for HTTP/2 and accepts the fallback.
/// </remarks>
public static class Networking
{
    /// <summary>
    /// An HTTP client that prefers HTTP/2 and still talks to a server that does not.
    /// </summary>
    /// <param name="timeout">Whole-request ceiling, including the body.</param>
    /// <param name="handler">Taken over when given; the client disposes it.</param>
    public static HttpClient CreateClient(TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = timeout;
        client.DefaultRequestVersion = HttpVersion.Version20;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        return client;
    }
}
