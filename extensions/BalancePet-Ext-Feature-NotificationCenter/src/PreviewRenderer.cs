using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BalancePet.NotificationCenter;

/// <summary>
/// Draws the hover ring off-screen so it can be looked at without hovering the pet.
/// </summary>
/// <remarks>
/// The ring only appears while the cursor is over the pet and Shift is held, which is a
/// hard thing to review and an impossible thing to review on a machine nobody is touching.
/// This lays it out for a chosen pet position, composes it over the desktop wallpaper and
/// the pet's own artwork, and writes a PNG — the same idea as the settings screengrab tool
/// in the main repository.
///
/// The composed background is the real wallpaper rather than a captured screen: a capture
/// would put whatever window happens to be open into the review, and would need the ring
/// to be on screen to take it.
/// </remarks>
internal static class PreviewRenderer
{
    internal const double PetSurfaceSize = 238;

    public static int Run(string outputPath, string position, IReadOnlyList<NotificationBubble> items, string? petImagePath)
    {
        var workArea = new Rect(
            SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top,
            SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        if (workArea.Width < 400 || workArea.Height < 400)
            workArea = new Rect(0, 0, 2048, 1123);

        var pet = PlacePet(position, workArea);

        // The backdrop first, because the ring chooses its text colour from what is behind
        // it. Composing it up front is what lets the preview measure the picture it is
        // actually making instead of the desktop underneath it.
        var backdrop = new RenderTargetBitmap(
            (int)Math.Round(workArea.Width), (int)Math.Round(workArea.Height), 96, 96, PixelFormats.Pbgra32);
        var backdropDrawing = new DrawingVisual();
        using (var context = backdropDrawing.RenderOpen())
        {
            DrawWallpaper(context, new Rect(0, 0, workArea.Width, workArea.Height));
            DrawPet(context, pet, new Rect(0, 0, workArea.Width, workArea.Height), petImagePath);
        }
        backdrop.Render(backdropDrawing);

        BubbleWindow.Diagnose = true;
        var ring = new BubbleWindow();
        ring.UpdateItems(items);
        // No waiting afterwards: the layout puts the items in their final state, because
        // the fade-in needs a running dispatcher and this runs on the thread that would
        // have to pump it.
        ring.BackdropLuminance = element => LuminanceBehind(backdrop, ring, element);
        ring.PreviewLayout(pet, workArea);

        // Both layers: the plate the values gather into and the wave that wakes them live
        // behind the items, and rendering only the items' layer is how the first preview came
        // out with no plate under the gathered row.
        var overlay = new RenderTargetBitmap(
            (int)Math.Round(workArea.Width), (int)Math.Round(workArea.Height), 96, 96, PixelFormats.Pbgra32);
        foreach (var layer in new FrameworkElement[] { ring.BehindCanvas, ring.InfoCanvas })
        {
            layer.Measure(new Size(workArea.Width, workArea.Height));
            layer.Arrange(new Rect(0, 0, workArea.Width, workArea.Height));
            layer.UpdateLayout();
        }
        // After the layout pass, not before: the plates take their colour from what is
        // behind them, which needs each item to have a size first.
        ring.RefreshAdaptiveContrast();
        overlay.Render(ring.BehindCanvas);
        overlay.Render(ring.InfoCanvas);

        // A window around the pet and its ring, not the whole desktop: the point is to look
        // at the ring, and a 2048-pixel-wide picture of mostly wallpaper is not a review.
        var region = Rect.Intersect(
            Rect.Union(pet, new Rect(pet.Left - 820, pet.Top - 420, pet.Width + 1560, pet.Height + 840)),
            workArea);

        var composed = new RenderTargetBitmap(
            (int)Math.Round(region.Width), (int)Math.Round(region.Height), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(backdrop, new Rect(-region.Left, -region.Top, workArea.Width, workArea.Height));
            context.DrawImage(overlay, new Rect(-region.Left, -region.Top, workArea.Width, workArea.Height));
        }
        composed.Render(drawing);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using (var stream = File.Create(outputPath)) encoder.Save(stream);

        Console.WriteLine($"预览已写出 {outputPath}");
        Console.WriteLine($"  画布 {composed.PixelWidth}×{composed.PixelHeight}  工作区 {workArea.Width:0}×{workArea.Height:0}  桌宠位置 {position}");
        Console.WriteLine($"  信息块 {items.Count} 条：" + string.Join(" / ", items.Select(item => item.Text)));
        return 0;
    }

    /// <summary>
    /// How bright the composed backdrop is behind one item. The item sits on a canvas whose
    /// origin is the work area's, which is also the backdrop bitmap's origin, so the two
    /// coordinate systems line up.
    /// </summary>
    private static double LuminanceBehind(RenderTargetBitmap backdrop, BubbleWindow ring, FrameworkElement element)
    {
        try
        {
            var origin = element.TranslatePoint(new Point(0, 0), ring.InfoCanvas);
            return LuminanceAt(backdrop, new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight));
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// How bright a rectangle of a composed backdrop is, in the same 0..1 the screen sampler
    /// reports. Shared with the sketches, which choose their plates by it.
    /// </summary>
    internal static double LuminanceAt(RenderTargetBitmap backdrop, Rect rect)
    {
        try
        {
            rect.Intersect(new Rect(0, 0, backdrop.PixelWidth, backdrop.PixelHeight));
            if (rect.Width < 2 || rect.Height < 2) return -1;

            var cropped = new CroppedBitmap(backdrop, new Int32Rect(
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
            var stride = cropped.PixelWidth * 4;
            var pixels = new byte[stride * cropped.PixelHeight];
            cropped.CopyPixels(pixels, stride, 0);

            double total = 0;
            for (var index = 0; index + 3 < pixels.Length; index += 4)
            {
                var blue = pixels[index] / 255.0;
                var green = pixels[index + 1] / 255.0;
                var red = pixels[index + 2] / 255.0;
                total += 0.2126 * red + 0.7152 * green + 0.0722 * blue;
            }
            return total / (pixels.Length / 4.0);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    internal static Rect PlacePet(string position, Rect workArea)
    {
        var margin = 24.0;
        return position switch
        {
            "center" => new Rect(
                workArea.Left + (workArea.Width - PetSurfaceSize) / 2,
                workArea.Top + (workArea.Height - PetSurfaceSize) / 2,
                PetSurfaceSize, PetSurfaceSize),
            "top-left" => new Rect(workArea.Left + margin, workArea.Top + margin, PetSurfaceSize, PetSurfaceSize),
            // Against the right edge but not in a corner: the case where the orbit is only
            // partly lost, which is the state a drag passes through.
            "edge" => new Rect(
                workArea.Right - PetSurfaceSize - margin,
                workArea.Top + (workArea.Height - PetSurfaceSize) / 2,
                PetSurfaceSize, PetSurfaceSize),
            // The usual place for a desktop pet, and the case the ring's corner fan exists
            // for: bottom right, with the taskbar below it.
            _ => new Rect(
                workArea.Right - PetSurfaceSize - margin,
                workArea.Bottom - PetSurfaceSize - margin,
                PetSurfaceSize, PetSurfaceSize)
        };
    }

    internal static void DrawWallpaper(DrawingContext context, Rect region)
    {
        var wallpaper = ReadWallpaperPath();
        if (wallpaper is not null)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(wallpaper);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                // Cover, not fit: the wallpaper fills the region the way it fills a screen.
                var scale = Math.Max(region.Width / image.PixelWidth, region.Height / image.PixelHeight);
                var width = image.PixelWidth * scale;
                var height = image.PixelHeight * scale;
                context.DrawImage(image, new Rect(
                    (region.Width - width) / 2, (region.Height - height) / 2, width, height));
                return;
            }
            catch (Exception)
            {
                // Falls through to the neutral fill: a missing wallpaper is not a reason to
                // fail a review.
            }
        }

        context.DrawRectangle(
            new LinearGradientBrush(Color.FromRgb(0x2B, 0x32, 0x3D), Color.FromRgb(0x16, 0x1A, 0x20), 90),
            null, new Rect(0, 0, region.Width, region.Height));
    }

    private static string? ReadWallpaperPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var path = key?.GetValue("WallPaper") as string;
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void DrawPet(DrawingContext context, Rect pet, Rect region, string? petImagePath)
    {
        var path = petImagePath ?? FindInstalledPetArtwork();
        if (path is null || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            context.DrawImage(image, new Rect(pet.Left - region.Left, pet.Top - region.Top, pet.Width, pet.Height));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The artwork of the appearance the program is currently set to, so the review shows
    /// the ring beside the pet it will actually appear beside.
    /// </summary>
    /// <summary>
    /// The artwork of an appearance that is actually installed, for the window's portrait.
    /// </summary>
    /// <remarks>
    /// Found by looking, rather than by reading which appearance is selected: the core's
    /// settings file does not expose a key this extension can rely on, and a guess at one
    /// would break silently. The newest installed package is the best available answer, and
    /// for a thirty-pixel portrait it is a good enough one. Null when nothing is installed,
    /// which the caller treats as "no portrait" rather than as an error.
    /// </remarks>
    internal static string? FindInstalledPetArtwork()
    {
        try
        {
            var extensions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BalancePet", "extensions");
            if (!Directory.Exists(extensions)) return null;
            return Directory.EnumerateFiles(extensions, "idle.png", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}pets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
