using System.IO;
using System.Net.Http;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Keeps <see cref="PetLineCatalog"/> supplied with the lines the appearance repository
/// publishes, so correcting what a character says does not require republishing its
/// artwork.
/// </summary>
/// <remarks>
/// This exists because the lines used to live only inside the package, and a package is
/// mostly artwork. Fixing a single word meant a new package version and a download of
/// several megabytes per appearance to deliver a few hundred bytes; measured across the
/// published set, giving all fourteen appearances their lines cost 147 MB of transfer for
/// 36 KB of text. Served from the repository instead, a line change is one small commit
/// and reaches every installation on its next refresh, with no release and no download.
///
/// Nothing here is load-bearing. The packages still carry their own copy, the neutral set
/// is still the last resort, and a fetch that fails changes nothing except that the lines
/// stay as they were — so failures are not reported to the user. There is no action to
/// offer: the appearance draws, speaks, and will simply pick the change up on a later
/// refresh.
/// </remarks>
public static class AppearanceLinesService
{
    /// <summary>Published beside the appearance catalog, from the same repository.</summary>
    public const string Url = "https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet-Pets/main/lines.json";

    /// <summary>
    /// Cached like the catalogs, and for the same reason: a launch without a network has
    /// to keep saying what the character said last time rather than reverting to the copy
    /// frozen inside the package.
    /// </summary>
    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "appearance-lines.json");

    /// <summary>
    /// Loads the cached copy, if any. Called before the first draw, so the window never
    /// shows the package's older lines and then swaps them.
    /// </summary>
    public static void LoadCache()
    {
        try
        {
            if (File.Exists(CachePath)) PetLineCatalog.PublishRemote(File.ReadAllText(CachePath));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
    }

    /// <summary>
    /// Fetches the document and, when it is usable, publishes and caches it.
    /// </summary>
    /// <remarks>
    /// Failure is deliberately silent, and the reason is that there is nothing to say: the
    /// appearance draws and speaks either way, the copies inside the packages are still
    /// there, and a later refresh retries. A message would only be a notification the user
    /// cannot act on, on a screen they did not open.
    /// </remarks>
    public static async Task RefreshAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            // Through the reader, so a network that refuses GitHub's raw host still gets
            // the served lines rather than keeping whatever the packages shipped.
            var fetched = await GitHubContentReader.DownloadAsync(
                http, Url, PetLineCatalog.MaxRemoteBytes, "application/json",
                "BalancePet-Appearance-Lines/1.0", cancellationToken);

            // Published before caching: an unwritable cache directory costs the next launch
            // nothing but a refetch, while refusing the document would cost this session the
            // corrected lines for no reason.
            if (!PetLineCatalog.PublishRemote(fetched.Text)) return;

            var directory = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(CachePath, fetched.Text);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or OperationCanceledException or InvalidOperationException or InvalidDataException)
        {
        }
    }
}
