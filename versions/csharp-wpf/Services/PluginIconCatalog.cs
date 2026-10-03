using System.Windows.Media;

namespace BalancePet.Wpf.Services;

/// <summary>
/// The line drawing a row uses when it has no picture to show.
/// </summary>
/// <param name="Stroked">The outline, drawn with a round-capped pen.</param>
/// <param name="Filled">A solid part, for the one glyph that needs one. Usually null.</param>
public sealed record PluginIcon(Geometry Stroked, Geometry? Filled);

/// <summary>
/// One icon per extension, and one per kind as the answer for everything else.
/// </summary>
/// <remarks>
/// This is not a second list of which extensions exist. It is a table of drawings
/// keyed by id, and an extension with no entry is not a missing extension — it gets
/// the drawing for its kind, which is why nothing has to be added here when one is
/// published. The other catalogs are the only place that answers what exists.
///
/// Every glyph is authored on the same 24x24 grid and shares one pen, so the set reads
/// as one family: the caller scales the whole 24 unit square rather than fitting each
/// drawing to its own bounds, because fitting per drawing would give the small ones a
/// heavier line than the large ones.
/// </remarks>
public static class PluginIconCatalog
{
    private const string Pet =
        "M8,10 A4,4 0 1 0 16,10 A4,4 0 1 0 8,10 Z " +
        "M5.5,20.5 A6.5,6.5 0 0 1 18.5,20.5";

    private const string Feature =
        "M3.5,3.5 H10 V10 H3.5 Z M14,3.5 H20.5 V10 H14 Z " +
        "M3.5,14 H10 V20.5 H3.5 Z M14,14 H20.5 V20.5 H14 Z";

    private const string Theme =
        "M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 Z M12,4 V20";

    private const string Browser =
        "M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 Z " +
        "M12,4 A4.2,8 0 1 0 12,20 A4.2,8 0 1 0 12,4 Z M4,12 H20";

    /// <summary>What the theme is: a window whose material is half of the point.</summary>
    private const string ThemeFill = "M12,4 A8,8 0 0 1 12,20 Z";

    private static readonly Dictionary<string, string> StrokeById = new(StringComparer.OrdinalIgnoreCase)
    {
        // Bars, because the extension counts tokens by day.
        ["balancepet.ext.feature.usage-analytics"] =
            "M3,20.5 H21 M5.75,20.5 V12.5 M10.25,20.5 V5.5 M14.75,20.5 V15.5 M19.25,20.5 V9",
        // The bubble the extension itself opens with.
        ["balancepet.ext.feature.notification-center"] =
            "M5,5 H19 C20.1,5 21,5.9 21,7 V15 C21,16.1 20.1,17 19,17 H12 L8,20.5 V17 H5 " +
            "C3.9,17 3,16.1 3,15 V7 C3,5.9 3.9,5 5,5 Z M7,9.5 H17 M7,12.8 H14",
        // Stacked surfaces: the material, not the palette.
        ["balancepet.theme.mica"] =
            "M9,4 H19.5 C20.3,4 21,4.7 21,5.5 V16 " +
            "M3.5,8 H14.5 C15.3,8 16,8.7 16,9.5 V18.5 C16,19.3 15.3,20 14.5,20 " +
            "H3.5 C2.7,20 2,19.3 2,18.5 V9.5 C2,8.7 2.7,8 3.5,8 Z M2,12 H16",
        // A browser window with what it read leaving it.
        ["balancepet.browser.bridge"] =
            "M1.5,5 H14.5 C15.3,5 16,5.7 16,6.5 V17.5 C16,18.3 15.3,19 14.5,19 H1.5 " +
            "C0.7,19 0,18.3 0,17.5 V6.5 C0,5.7 0.7,5 1.5,5 Z M0,8.5 H16 " +
            "M3.2,6.75 A0.6,0.6 0 1 1 3.21,6.75 Z M17.5,12 H23 M20.6,9.6 L23,12 L20.6,14.4"
    };

    private static readonly Dictionary<string, string> StrokeByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pet"] = Pet,
        ["theme"] = Theme,
        ["browser"] = Browser,
        ["feature"] = Feature
    };

    private static readonly Dictionary<string, PluginIcon> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>The drawing for one entry, or for its kind.</summary>
    public static PluginIcon Resolve(string? type, string? id)
    {
        var kind = (type ?? "").Trim();
        var key = (id ?? "").Trim();
        var cacheKey = $"{kind}|{key}";

        lock (Gate)
        {
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        var stroke = StrokeById.TryGetValue(key, out var specific) ? specific
            : StrokeByType.TryGetValue(kind, out var fallback) ? fallback
            : Feature;
        var fill = string.Equals(key, "balancepet.theme.mica", StringComparison.OrdinalIgnoreCase)
            ? ThemeFill
            : null;

        var icon = new PluginIcon(Freeze(stroke), fill is null ? null : Freeze(fill));
        lock (Gate) { Cache[cacheKey] = icon; }
        return icon;
    }

    /// <summary>
    /// Frozen because one instance is handed to every row that asks for it. A frozen
    /// Freezable is the only kind that can be shared between elements without WPF
    /// treating each use as a change to the others.
    /// </summary>
    private static Geometry Freeze(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}
