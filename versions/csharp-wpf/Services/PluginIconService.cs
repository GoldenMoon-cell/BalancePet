using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Fetches and caches the small pictures a catalog entry points at.
/// </summary>
/// <remarks>
/// The list has to draw an appearance it has not downloaded, so the picture cannot
/// come out of the package: a package is twelve megabytes, and previewing sixteen of
/// them to fill a list would cost more than the list is worth. The catalogs therefore
/// name a picture, and this is what reads it.
///
/// Nothing here is load-bearing. A row without a picture draws its kind's glyph, and
/// a fetch that fails leaves the row exactly that: an appearance that cannot be
/// previewed is still an appearance that can be installed. So a failure is not
/// reported — there is no action to offer, and a message about a missing thumbnail
/// on a screen the user opened to install something would only be noise.
///
/// Cached on disk keyed by the entry's id and version, not by its id alone: a
/// republished appearance has redrawn art, and an icon cached under the old version
/// would keep showing the old face after the update that changed it.
/// </remarks>
public sealed class PluginIconService
{
    /// <summary>A preview is a small crop. Anything larger is not one.</summary>
    private const int MaxBytes = 1024 * 1024;

    /// <summary>
    /// Decoded at twice the size the list draws, so it stays sharp on a scaled
    /// display, and no larger: these arrive sixteen at a time.
    /// </summary>
    private const int DecodeWidth = 64;

    /// <summary>
    /// The square a state image is cropped to, as fractions of the image.
    /// </summary>
    /// <remarks>
    /// Mirrored in <c>tools/make-appearance-previews.py</c>, which crops the same
    /// square out of the published packages to build the pictures the catalog serves.
    /// The two have to agree, or the same appearance would wear two different faces
    /// depending on whether its picture came over the network.
    /// </remarks>
    private static readonly (double Side, double CentreX, double CentreY) DefaultCrop = (0.64, 0.50, 0.48);

    /// <summary>Appearances whose face the default square lands beside.</summary>
    private static readonly Dictionary<string, (double Side, double CentreX, double CentreY)> CropOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ernie's face sits left of centre, and perplexity's left and high.
            ["ernie"] = (0.60, 0.42, 0.52),
            ["perplexity"] = (0.60, 0.44, 0.46)
        };

    /// <summary>
    /// The size a state image is decoded at before cropping. The tile is 32 px and a
    /// scaled display wants twice that; this leaves the crop plenty and still keeps a
    /// megabyte of artwork from being unpacked at full size for every row.
    /// </summary>
    private const int PreviewDecodeWidth = 256;

    private readonly Dictionary<string, ImageSource> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public string CacheDirectory { get; }

    /// <summary>
    /// How much the icon cache is using, for the advanced page to show.
    /// </summary>
    /// <remarks>
    /// Walked rather than tracked: a number kept alongside the directory drifts the moment
    /// anything else touches it, and this is one directory of a few dozen small files. The
    /// answer wanted here is honest, not instant.
    /// </remarks>
    public long CacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return 0;
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
            }
            return total;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Forgets every cached icon: the files, and the pictures already decoded from them.
    /// </summary>
    /// <remarks>
    /// Both, or neither. Emptying the directory while leaving the decoded images in memory
    /// produces the one outcome worse than a full cache — a list still showing pictures whose
    /// files the user believes they just deleted, with nothing to re-fetch them from on a
    /// machine that has since gone offline.
    /// </remarks>
    public void ClearCache()
    {
        lock (_gate)
        {
            _images.Clear();
            _failed.Clear();
        }
        try
        {
            if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public PluginIconService(string? cacheDirectory = null)
        => CacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BalancePet", "plugin-icons");

    /// <summary>Whether the entry can be asked for a picture at all.</summary>
    public static bool CanHaveIcon(PluginCatalogRecord record) => !string.IsNullOrWhiteSpace(record.IconUrl);

    /// <summary>
    /// Whether there is still a picture to be had for this entry, from anywhere.
    /// </summary>
    /// <remarks>
    /// Asked by the caller to decide whether to keep going, so it has to count the
    /// appearance's own artwork as well: that source needs no network and cannot
    /// fail, and a pass that only looked for published pictures would leave those
    /// rows turning forever.
    /// </remarks>
    public bool NeedsWork(PluginCatalogRecord record, string? installedStyle)
    {
        if (Cached(record) is not null || HasFailed(record)) return false;
        return CanHaveIcon(record) || !string.IsNullOrWhiteSpace(installedStyle);
    }

    /// <summary>
    /// The picture if it is already in memory, without touching the disk or the
    /// network. Used while building the list, so the first paint has no animation in
    /// it for anything already fetched.
    /// </summary>
    public ImageSource? Cached(PluginCatalogRecord record)
    {
        lock (_gate)
        {
            return _images.TryGetValue(Key(record), out var image) ? image : null;
        }
    }

    public bool HasFailed(PluginCatalogRecord record)
    {
        lock (_gate)
        {
            return _failed.Contains(Key(record));
        }
    }

    /// <summary>
    /// Reads every published picture already on disk into memory.
    /// </summary>
    /// <remarks>
    /// Called once when the catalog arrives rather than per row, because the cost that
    /// matters is the one in front of the user: decoding files while the extension page
    /// is being laid out would be paid every time it is opened. Only the published
    /// pictures are read here — they are 128 px crops. An appearance's own artwork is
    /// decoded by the resolve pass instead, off the UI thread, because a state image
    /// is a megabyte and sixteen of them are not something to do between two keystrokes.
    /// </remarks>
    public void LoadFromDisk(IEnumerable<PluginCatalogRecord> records)
    {
        foreach (var record in records)
        {
            if (!CanHaveIcon(record)) continue;
            var path = CachePath(record);
            try
            {
                if (!File.Exists(path)) continue;
                var bytes = File.ReadAllBytes(path);
                var image = Decode(bytes);
                if (image is null) continue;
                lock (_gate) { _images[Key(record)] = image; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The picture for one entry, from wherever it can come from.
    /// </summary>
    /// <remarks>
    /// Two sources, cheapest first. The appearance's own artwork is on the disk and
    /// needs no network, which matters more than it sounds: the catalogs are served
    /// from a host a network can refuse while leaving the rest of the internet
    /// reachable, and in that state an installed appearance has to draw itself or
    /// every row shows the same mark — which is the thing this pass exists to stop. A
    /// published picture is second because it is the only source that can fail.
    /// </remarks>
    public async Task<ImageSource?> ResolveAsync(
        HttpClient http,
        PluginCatalogRecord record,
        string? installedStyle,
        CancellationToken cancellationToken = default)
    {
        var cached = Cached(record);
        if (cached is not null) return cached;

        // The published icon first, when the entry has one — including for appearances that are
        // already installed. Both lists then draw the same picture for the same character: the
        // store row used the published icon and the installed row used a face cropped out of the
        // state artwork, and side by side they read as two different things.
        //
        // The crop stays as the fallback, and it is not decorative: an entry with no icon_url, an
        // offline machine, or a URL that has gone stale all land on it, and a list with nothing
        // to draw in those cases would be worse than a list drawn two ways.
        if (CanHaveIcon(record))
        {
            var fetched = await FetchAsync(http, record, cancellationToken);
            if (fetched is not null) return fetched;
            if (cancellationToken.IsCancellationRequested) return null;
        }

        // Off the UI thread: reading and downscaling a state image is tens of
        // milliseconds, and sixteen rows of it is a visible stall on a page that is
        // being drawn at the same time. Not cancellable: it is a local file, it is
        // already paid for, and abandoning it on the way out would leave a row
        // spinning for a picture the process already had in hand.
        var local = await Task.Run(() => BuildLocalPreview(style: installedStyle));
        if (local is not null)
        {
            lock (_gate) { _images[Key(record)] = local; }
            return local;
        }

        return null;
    }

    /// <summary>
    /// A picture made from the artwork already on this machine.
    /// </summary>
    /// <remarks>
    /// The list draws an appearance at 32 px and the state image is a full-figure
    /// portrait, so a whole-figure thumbnail leaves the face a seventh of the tile and
    /// the character a coloured smudge. The tile shows the face instead, found by a
    /// fixed square: these portraits are chibi busts drawn to one framing convention,
    /// and the few the square misses are listed in <see cref="CropOverrides"/>.
    /// </remarks>
    private static ImageSource? BuildLocalPreview(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return null;
        try
        {
            // A package may carry a portrait of its own for this list — a square head, the same
            // tile the extensions use — and it is preferred over the artwork's first frame when
            // it is there. Optional on purpose: the sixteen appearances already published have
            // no such file, and one missing file must not cost them their thumbnail.
            var directory = PetStyleCatalog.ResolveAssetDirectory(style);
            var portrait = Path.Combine(directory, "logo.png");
            var path = File.Exists(portrait) ? portrait : Path.Combine(directory, "idle.png");
            if (!File.Exists(path)) return null;
            return BuildPreview(path, style);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The square a state image is cropped to, and the tile built from it.
    /// </summary>
    /// <param name="statePath">One of the nine state images.</param>
    /// <param name="style">Appearance id, for the entries in <see cref="CropOverrides"/>.</param>
    public static ImageSource? BuildPreview(string statePath, string? style)
    {
        // Decoded small rather than decoded and then shrunk: the crop only ever keeps
        // a fraction of the image, and a decoder told the size it needs does not have
        // to hand back four megabytes for each of sixteen rows.
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        source.DecodePixelWidth = PreviewDecodeWidth;
        source.UriSource = new Uri(Path.GetFullPath(statePath));
        source.EndInit();
        source.Freeze();

        var (side, centreX, centreY) = CropOverrides.TryGetValue(style ?? "", out var custom) ? custom : DefaultCrop;
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var available = Math.Min(width, height);
        var span = side * available;
        if (span < 1) return null;

        var left = Math.Clamp((int)Math.Round(centreX * width - span / 2), 0, Math.Max(0, width - (int)span));
        var top = Math.Clamp((int)Math.Round(centreY * height - span / 2), 0, Math.Max(0, height - (int)span));
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, (int)span, (int)span));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>
    /// Downloads the picture the catalog published, when the entry has one.
    /// </summary>
    /// <returns>The picture, or null when there is none to be had.</returns>
    public async Task<ImageSource?> FetchAsync(
        HttpClient http,
        PluginCatalogRecord record,
        CancellationToken cancellationToken = default)
    {
        if (!CanHaveIcon(record)) return null;

        var key = Key(record);
        lock (_gate)
        {
            if (_images.TryGetValue(key, out var cached)) return cached;
            // Tried and failed once this session. Retrying per row would turn a
            // network that is down into sixteen timeouts on a page that draws fine
            // without any of them.
            if (_failed.Contains(key)) return null;
        }

        byte[]? bytes = null;
        try
        {
            // Through the reader, which tries the declared address and then the mirror of
            // the same repository: the host this catalog points at is one a network can
            // refuse while leaving the rest of GitHub reachable, and a row that cannot be
            // drawn is exactly what this pass exists to prevent.
            var fetched = await GitHubContentReader.DownloadAsync(
                http, record.IconUrl, MaxBytes, "image/png",
                "BalancePet-Plugin-Icons/1.0", cancellationToken);
            bytes = fetched.Bytes;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException
            or TaskCanceledException or OperationCanceledException or InvalidOperationException or UriFormatException)
        {
            // A cancelled fetch is not a failed one: the window is closing, and
            // remembering it as unreachable would be a conclusion about the network
            // drawn from the user clicking a button.
            if (!cancellationToken.IsCancellationRequested) lock (_gate) { _failed.Add(key); }
            return null;
        }

        var image = bytes is null ? null : Decode(bytes);
        if (image is null)
        {
            lock (_gate) { _failed.Add(key); }
            return null;
        }

        lock (_gate) { _images[key] = image; }

        // Written after the picture is in memory: a cache directory that cannot be
        // written costs the next launch one download, while refusing the picture
        // because it could not be stored would cost this session the picture.
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.WriteAllBytes(CachePath(record), bytes!);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return image;
    }

    public string CachePath(PluginCatalogRecord record)
        => Path.Combine(CacheDirectory, $"{Safe(record.Id)}-{Safe(record.Version)}.png");

    private static string Key(PluginCatalogRecord record) => $"{record.Id}|{record.Version}";
    /// <summary>Ids and versions are already restricted, but a cache path is a path.</summary>
    private static string Safe(string? value)
        => new((value ?? "").Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_').ToArray());

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            // OnLoad, so the bitmap does not keep the stream or the file open, and
            // frozen, so the same instance can be handed to every row that shows it.
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = DecodeWidth;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or FileFormatException or OverflowException)
        {
            return null;
        }
    }
}
