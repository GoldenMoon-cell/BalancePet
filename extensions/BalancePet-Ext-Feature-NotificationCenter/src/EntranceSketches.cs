using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BalancePet.NotificationCenter;

/// <summary>
/// Three ways the hover plates could arrive, rendered as frames.
/// </summary>
/// <remarks>
/// The plates appear when the pet is hovered with Shift held, so how they arrive is the
/// first thing anyone sees of them — and "fade in" is the one answer that says nothing about
/// the pet. Each style here is a different physical story: breathed out from the pet, dropped
/// and landing, or woken one at a time by a wave leaving the pet. Rendered as sequences
/// because the difference between them is entirely in the timing.
/// </remarks>
internal static class EntranceSketches
{
    private const double PlateWidth = 196;
    private const double PlateHeight = 46;

    public static int Render(string outputDirectory, string style, int frames, string position, string? petImagePath, bool forceDark)
    {
        var workArea = new Rect(0, 0, Math.Max(1200, SystemParameters.WorkArea.Width), Math.Max(760, SystemParameters.WorkArea.Height));
        var pet = PreviewRenderer.PlacePet(position, workArea);

        var backdrop = new RenderTargetBitmap(
            (int)Math.Round(workArea.Width), (int)Math.Round(workArea.Height), 96, 96, PixelFormats.Pbgra32);
        var painting = new DrawingVisual();
        using (var context = painting.RenderOpen())
        {
            PreviewRenderer.DrawWallpaper(context, new Rect(0, 0, workArea.Width, workArea.Height));
            PreviewRenderer.DrawPet(context, pet, new Rect(0, 0, workArea.Width, workArea.Height), petImagePath);
        }
        backdrop.Render(painting);

        var region = Rect.Intersect(
            new Rect(pet.Left - 560, pet.Top - 340, pet.Width + 1120, pet.Height + 680), workArea);
        System.IO.Directory.CreateDirectory(outputDirectory);

        var segments = new (string Value, string Kind)[]
        {
            ("42.80 CNY", "balance"), ("官方登录", "account"), ("工作中", "task"), ("v1.6.0", "system")
        };
        var slots = BubbleWindow.SelectOrbitSlots(pet, workArea, segments.Length, null);
        var petCentre = new Point(pet.Left + pet.Width / 2, pet.Top + pet.Height / 2);
        var farthest = slots.Count == 0
            ? 1
            : slots.Max(slot => Distance(petCentre, new Point(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2)));

        for (var index = 0; index < frames; index++)
        {
            var progress = frames == 1 ? 1 : index / (double)(frames - 1);
            var composed = new RenderTargetBitmap(
                (int)Math.Round(region.Width), (int)Math.Round(region.Height), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawImage(backdrop, new Rect(-region.Left, -region.Top, workArea.Width, workArea.Height));
                DrawFrame(context, style, progress, segments, slots, region, backdrop, petCentre, farthest, forceDark);
            }
            composed.Render(drawing);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composed));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(outputDirectory, $"frame-{index:00}.png"));
            encoder.Save(stream);
        }

        Console.WriteLine($"入场帧已写出 {frames} 张（{style}）到 {outputDirectory}");
        return 0;
    }

    private static void DrawFrame(
        DrawingContext context, string style, double progress,
        (string Value, string Kind)[] segments, IReadOnlyList<Rect> slots, Rect region,
        RenderTargetBitmap backdrop, Point petCentre, double farthest, bool forceDark)
    {
        // The wave, for the ripple style: one ring leaving the pet, its radius the clock.
        if (style == "ripple")
        {
            var radius = progress * (farthest + 70);
            var fade = (1 - progress) * 0.95;
            if (fade > 0.01)
            {
                context.DrawEllipse(null,
                    new Pen(new SolidColorBrush(Color.FromArgb((byte)(fade * 255), 0x2D, 0xE1, 0xC2)), 2.6),
                    new Point(petCentre.X - region.Left, petCentre.Y - region.Top), radius, radius);
                context.DrawEllipse(null,
                    new Pen(new SolidColorBrush(Color.FromArgb((byte)(fade * 200), 0xFF, 0xFF, 0xFF)), 1.4),
                    new Point(petCentre.X - region.Left, petCentre.Y - region.Top), radius * 0.92, radius * 0.92);
            }
        }

        for (var index = 0; index < segments.Length && index < slots.Count; index++)
        {
            var slot = slots[index];
            var target = new Rect(
                slot.Left + (slot.Width - PlateWidth) / 2, slot.Top + (slot.Height - PlateHeight) / 2,
                PlateWidth, PlateHeight);

            // Per style: where it comes from, when it sets off, and whether it lands.
            double local;
            var offset = new Vector(0, 0);
            var scale = 1.0;
            var squash = 0.0;
            switch (style)
            {
                case "drip":
                    local = Math.Clamp((progress - index * 0.10) / 0.58, 0, 1);
                    // Falls under its own weight: quick at the end, not at the start.
                    offset.Y = -30 * (1 - local * local);
                    var landing = Math.Clamp((local - 0.82) / 0.18, 0, 1);
                    squash = Math.Sin(landing * Math.PI) * 0.10;
                    scale = 0.94 + 0.06 * local;
                    break;
                case "ripple":
                    var distance = Distance(petCentre, new Point(target.Left + PlateWidth / 2, target.Top + PlateHeight / 2));
                    var reach = progress * (farthest + 70);
                    local = Math.Clamp((reach - (distance - 46)) / 46, 0, 1);
                    // Nudged outward as it wakes, so it reads as pushed by the wave.
                    var direction = new Vector(target.Left + PlateWidth / 2 - petCentre.X, target.Top + PlateHeight / 2 - petCentre.Y);
                    if (direction.Length > 0.01) direction.Normalize();
                    offset = direction * (1 - EaseOutCubic(local)) * 14;
                    scale = 0.92 + 0.08 * local;
                    break;
                default: // breathe
                    local = Math.Clamp((progress - index * 0.05) / 0.72, 0, 1);
                    var eased = EaseOutBack(local);
                    var from = new Point(petCentre.X - PlateWidth / 2, petCentre.Y - PlateHeight / 2);
                    offset = new Vector(
                        (from.X - target.Left) * (1 - eased), (from.Y - target.Top) * (1 - eased));
                    scale = 0.45 + 0.55 * eased;
                    break;
            }

            var alpha = Math.Clamp(local * (style == "drip" ? 2.4 : 1.5), 0, 1);
            if (alpha <= 0.01) continue;

            var rect = new Rect(target.Left + offset.X, target.Top + offset.Y, PlateWidth, PlateHeight);
            var centre = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            var drawn = new Rect(0, 0, rect.Width * scale * (1 + squash * 0.6), rect.Height * scale * (1 - squash));
            drawn.Offset(centre.X - drawn.Width / 2, centre.Y - drawn.Height / 2);

            var measured = PreviewRenderer.LuminanceAt(backdrop, new Rect(
                rect.Left + region.Left, rect.Top + region.Top, rect.Width, rect.Height));
            var dark = forceDark || measured > 0.55;
            var kind = Kind(segments[index].Kind, dark);
            var localRect = new Rect(drawn.Left - region.Left, drawn.Top - region.Top, drawn.Width, drawn.Height);

            context.PushOpacity(alpha);
            context.DrawRoundedRectangle(
                new SolidColorBrush(dark ? Color.FromArgb(0xEE, 0x14, 0x1C, 0x26) : Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF)),
                new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0x0F, 0x17, 0x2A)), 1),
                localRect, 12, 12);
            context.DrawRoundedRectangle(new SolidColorBrush(kind), null,
                new Rect(localRect.Left + 12, localRect.Top + localRect.Height / 2 - 3, 6, 6), 3, 3);
            var text = Label(segments[index].Value, 13, dark ? Color.FromRgb(0xF8, 0xFA, 0xFC) : Color.FromRgb(0x0F, 0x17, 0x2A));
            context.DrawText(text, new Point(localRect.Left + 24, localRect.Top + (localRect.Height - text.Height) / 2));
            context.Pop();
        }
    }

    private static double Distance(Point first, Point second)
        => Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>Overshoots a little: something arriving, not something being placed.</summary>
    private static double EaseOutBack(double t)
    {
        const double c1 = 1.35;
        const double c3 = c1 + 1;
        var p = t - 1;
        return 1 + c3 * p * p * p + c1 * p * p;
    }

    private static Color Kind(string kind, bool dark) => kind switch
    {
        "balance" => dark ? Color.FromRgb(0x2D, 0xE1, 0xC2) : Color.FromRgb(0x07, 0x8C, 0x82),
        "task" => dark ? Color.FromRgb(0x78, 0xA9, 0xFF) : Color.FromRgb(0x34, 0x4F, 0x91),
        "account" => dark ? Color.FromRgb(0xFB, 0xBF, 0x24) : Color.FromRgb(0xB4, 0x53, 0x09),
        _ => dark ? Color.FromRgb(0x94, 0xA3, 0xB8) : Color.FromRgb(0x47, 0x55, 0x69)
    };

    private static FormattedText Label(string value, double size, Color colour)
        => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, new SolidColorBrush(colour), 1.0);
}
