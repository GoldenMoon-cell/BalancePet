using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BalancePet.NotificationCenter;

/// <summary>
/// Sketches of where the hover information could go, drawn over the same desk as the
/// current design so the three can be compared directly.
/// </summary>
/// <remarks>
/// Drawings, not the working ring: the point of a mockup is to answer "does this look like
/// it belongs to the pet" before anything is rebuilt, and the arrangement, the plate shapes
/// and the type sizes here are the whole of what is being asked. Each sketch reuses the
/// preview's backdrop — the real wallpaper and the pet's own artwork — because a mockup on a
/// blank canvas answers a question nobody asked.
/// </remarks>
internal static class RingSketches
{
    // Colours are the settings window's: the same slate text, the same muted secondary, the
    // same accent family, so a sketch that looks right is a sketch that can be built.
    private static readonly Color Ink = Color.FromRgb(0x0F, 0x17, 0x2A);
    private static readonly Color InkMuted = Color.FromRgb(0x47, 0x55, 0x69);
    private static readonly Color Paper = Color.FromRgb(0xF8, 0xFA, 0xFC);
    private static readonly Color PaperMuted = Color.FromRgb(0xC7, 0xD2, 0xE0);

    public static int Run(string outputPath, string variant, string position, string? petImagePath, bool forceDark = false)
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

        // A window around the pet, wide enough for whatever the sketch puts beside it.
        var region = Rect.Intersect(
            Rect.Union(pet, new Rect(pet.Left - 700, pet.Top - 360, pet.Width + 900, pet.Height + 620)),
            workArea);

        var composed = new RenderTargetBitmap(
            (int)Math.Round(region.Width), (int)Math.Round(region.Height), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(backdrop, new Rect(-region.Left, -region.Top, workArea.Width, workArea.Height));
            var localPet = new Rect(pet.Left - region.Left, pet.Top - region.Top, pet.Width, pet.Height);
            switch (variant)
            {
                case "b":
                case "b2":
                case "b3": Strip(context, backdrop, region, localPet, variant, forceDark); break;
                case "c": Pill(context, backdrop, region, localPet, "本次消耗 7.70 CNY", "balance"); break;
                case "c-notice": Pill(context, backdrop, region, localPet, "有新的更新记录 · 2 条", "system"); break;
                default: Cards(context, backdrop, region, localPet); break;
            }
        }
        composed.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using (var stream = System.IO.File.Create(outputPath)) encoder.Save(stream);
        Console.WriteLine($"草图已写出 {outputPath}  {composed.PixelWidth}×{composed.PixelHeight}");
        return 0;
    }

    /// <summary>
    /// A: the same four items around the pet, each with a plate of its own.
    /// </summary>
    /// <remarks>
    /// The arrangement is left as it is on purpose, so the comparison isolates the one
    /// change: an item that carries its own background cannot lose its contrast to whatever
    /// the wallpaper happens to be doing, and it stops reading as debug text.
    /// </remarks>
    private static void Cards(DrawingContext context, RenderTargetBitmap backdrop, Rect region, Rect pet)
    {
        var cards = new (string Primary, string Detail, string Kind, double Left, double Top)[]
        {
            ("余额 42.80 CNY", "上次 35.10 · 本次消耗 7.70", "balance", pet.Left - 236, pet.Top - 74),
            ("当前登录方式 · 官方登录", "官方 API · DeepSeek", "account", pet.Left - 250, pet.Top + 34),
            ("DeepSeek 工作中", "正在处理", "task", pet.Left - 226, pet.Top + 142),
            ("当前版本 · 1.5.1", "BalancePet", "system", pet.Left + 6, pet.Top - 82)
        };

        foreach (var card in cards)
        {
            var rect = new Rect(card.Left, card.Top, 214, 54);
            var dark = PreviewRenderer.LuminanceAt(backdrop, new Rect(
                rect.Left + region.Left, rect.Top + region.Top, rect.Width, rect.Height)) > 0.55;
            var fill = new SolidColorBrush(dark ? Color.FromArgb(0xE8, 0x14, 0x1C, 0x26) : Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
            var edge = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x0F, 0x17, 0x2A)), 1);
            context.DrawRoundedRectangle(fill, edge, rect, 9, 9);
            // The accent moves to a bar down the leading edge: a colour that identifies the
            // kind, rather than a rule that asks to be read as an underline.
            context.DrawRoundedRectangle(new SolidColorBrush(Accent(card.Kind, dark)),
                null, new Rect(rect.Left + 8, rect.Top + 12, 3, rect.Height - 24), 1.5, 1.5);
            context.DrawText(Text(card.Primary, 13, dark ? Paper : Ink, semibold: true),
                new Point(rect.Left + 19, rect.Top + 9));
            context.DrawText(Text(card.Detail, 11, dark ? PaperMuted : InkMuted),
                new Point(rect.Left + 19, rect.Top + 31));
        }
    }

    /// <summary>
    /// B: one plate, joined to the pet rather than scattered around it.
    /// </summary>
    /// <remarks>
    /// Four sentences around a character are four things that happen to be near it; one
    /// plate joined to it is a caption, and a caption belongs to what it describes. Values
    /// rather than sentences, because a caption has no room for prose — and the detail lines
    /// they replace were the least readable part of the current design.
    ///
    /// Three placements, because the first attempt put the plate level with the pet's face,
    /// where it read as something in front of the character rather than attached to it, and
    /// its connector was too faint to see.
    /// </remarks>
    private static void Strip(DrawingContext context, RenderTargetBitmap backdrop, Rect region, Rect pet, string style, bool forceDark)
    {
        var side = style != "b3";
        var height = side ? 52.0 : 40.0;
        var width = side ? 452.0 : pet.Width + 76;
        // Beside the body and low, level with the feet. The caption variant lies over the
        // lower edge of the artwork on purpose, so the plate belongs to the pet's outline
        // instead of hovering next to it.
        var rect = side
            ? new Rect(pet.Left - width - 20, pet.Bottom - height - 30, width, height)
            : new Rect(pet.Left + (pet.Width - width) / 2, pet.Bottom - height - 14, width, height);

        var measured = PreviewRenderer.LuminanceAt(backdrop, new Rect(
            rect.Left + region.Left, rect.Top + region.Top, rect.Width, rect.Height));
        var dark = forceDark || measured > 0.55;
        var fill = new SolidColorBrush(dark
            ? Color.FromArgb(side ? (byte)0xEE : (byte)0xDC, 0x14, 0x1C, 0x26)
            : Color.FromArgb(side ? (byte)0xF5 : (byte)0xE6, 0xFF, 0xFF, 0xFF));
        var edge = new Pen(new SolidColorBrush(dark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x22, 0x0F, 0x17, 0x2A)), 1);
        context.DrawRoundedRectangle(fill, edge, rect, side ? 14 : 12, side ? 14 : 12);

        if (side)
        {
            // The join, made visible this time: a bar from the plate to the pet and a dot
            // where it lands, so the plate reads as attached rather than as nearby.
            var accent = Accent("balance", dark);
            var brush = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B));
            var mid = rect.Top + height / 2;
            context.DrawRoundedRectangle(brush, null,
                new Rect(rect.Right, mid - 1.5, pet.Left - rect.Right + 6, 3), 1.5, 1.5);
            context.DrawEllipse(brush, null, new Point(pet.Left + 6, mid), 4, 4);
        }

        var segments = new (string Value, string Kind)[]
        {
            ("42.80 CNY", "balance"), ("官方登录", "account"), ("工作中", "task"), ("v1.5.1", "system")
        };
        var size = side ? 13.0 : 12.0;
        var texts = segments
            .Select(segment => Text(segment.Value, size, dark ? Paper : Ink, semibold: segment.Kind == "balance"))
            .ToArray();
        // Measured, not guessed: the row is centred as a whole and the dividers land between
        // the groups rather than through them.
        var needed = texts.Sum(text => 12 + text.Width) + 24 * (segments.Length - 1);
        var cursor = rect.Left + Math.Max(16, (rect.Width - needed) / 2);
        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
            {
                context.DrawRectangle(
                    new SolidColorBrush(dark ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x0F, 0x17, 0x2A)),
                    null, new Rect(cursor - 12, rect.Top + 14, 1, height - 28));
            }
            context.DrawRoundedRectangle(new SolidColorBrush(Accent(segments[index].Kind, dark)),
                null, new Rect(cursor, rect.Top + height / 2 - 3, 6, 6), 3, 3);
            context.DrawText(texts[index], new Point(cursor + 12, rect.Top + (height - texts[index].Height) / 2));
            cursor += 12 + texts[index].Width + 24;
        }
    }

    /// <summary>
    /// C: nothing until there is something, and then one line of it.
    /// </summary>
    /// <remarks>
    /// The current ring always shows four slots, so most of the time it is reporting that
    /// nothing has changed. This shows one thing that just happened, above the pet, and is
    /// absent otherwise -- which is what makes it worth looking at when it does appear.
    /// </remarks>
    private static void Pill(DrawingContext context, RenderTargetBitmap backdrop, Rect region, Rect localPet, string text, string kind)
    {
        var label = Text(text, 13, Ink, semibold: true);
        var height = 38.0;
        var width = label.Width + 44;
        var rect = new Rect(
            localPet.Left + (localPet.Width - width) / 2, localPet.Top - height - 16, width, height);
        var dark = PreviewRenderer.LuminanceAt(backdrop, new Rect(
            rect.Left + region.Left, rect.Top + region.Top, rect.Width, rect.Height)) > 0.55;
        var fill = new SolidColorBrush(dark ? Color.FromArgb(0xEE, 0x14, 0x1C, 0x26) : Color.FromArgb(0xF5, 0xFF, 0xFF, 0xFF));
        var edge = new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x22, 0x0F, 0x17, 0x2A)), 1);
        context.DrawRoundedRectangle(fill, edge, rect, height / 2, height / 2);
        context.DrawRoundedRectangle(new SolidColorBrush(Accent(kind, dark)),
            null, new Rect(rect.Left + 16, rect.Top + height / 2 - 3.5, 7, 7), 3.5, 3.5);
        var ink = Text(text, 13, dark ? Paper : Ink, semibold: true);
        context.DrawText(ink, new Point(rect.Left + 31, rect.Top + (height - ink.Height) / 2));
    }

    /// <summary>
    /// The idea in frames: four plates around the pet while there is room, gathering into
    /// one plate when the pet is in a corner.
    /// </summary>
    /// <remarks>
    /// Rendered as a sequence because the point of it is the movement — droplets running
    /// together — and a single picture of the end state cannot say whether that reads. One
    /// file per frame, for the caller to assemble into something that moves.
    ///
    /// The items are placed by the ring's own slot algorithm, not by hand, so the frames
    /// show the arrangement the program would actually choose; only the interpolation
    /// towards the merged plate is drawn here.
    /// </remarks>
    public static int RenderMorph(string outputDirectory, int frames, string position, string? petImagePath, bool forceDark)
    {
        var workArea = new Rect(0, 0, Math.Max(1200, SystemParameters.WorkArea.Width), Math.Max(760, SystemParameters.WorkArea.Height));
        var pet = PreviewRenderer.PlacePet(position == "corner" ? "bottom-right" : position, workArea);

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
            Rect.Union(pet, new Rect(pet.Left - 700, pet.Top - 420, pet.Width + 900, pet.Height + 700)),
            workArea);
        System.IO.Directory.CreateDirectory(outputDirectory);

        var segments = new (string Value, string Kind)[]
        {
            ("42.80 CNY", "balance"), ("官方登录", "account"), ("工作中", "task"), ("v1.5.1", "system")
        };
        var slots = BubbleWindow.SelectOrbitSlots(pet, workArea, segments.Length, null);
        var merged = MergedPlate(pet);
        var measured = PreviewRenderer.LuminanceAt(backdrop, new Rect(
            merged.Left + region.Left, merged.Top + region.Top, merged.Width, merged.Height));
        var dark = forceDark || measured > 0.55;

        for (var index = 0; index < frames; index++)
        {
            var progress = frames == 1 ? 1 : index / (double)(frames - 1);
            var composed = new RenderTargetBitmap(
                (int)Math.Round(region.Width), (int)Math.Round(region.Height), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawImage(backdrop, new Rect(-region.Left, -region.Top, workArea.Width, workArea.Height));
                MorphFrame(context, segments, slots, merged, region, progress, dark);
            }
            composed.Render(drawing);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composed));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(outputDirectory, $"frame-{index:00}.png"));
            encoder.Save(stream);
        }

        Console.WriteLine($"形变帧已写出 {frames} 张到 {outputDirectory}");
        return 0;
    }

    /// <summary>Where the four values end up once the plates have run together.</summary>
    private static Rect MergedPlate(Rect pet)
    {
        const double width = 452;
        const double height = 52;
        return new Rect(pet.Left - width - 20, pet.Bottom - height - 30, width, height);
    }

    /// <summary>
    /// One frame of the gathering.
    /// </summary>
    /// <remarks>
    /// Each item travels from its slot to the place it will hold inside the merged plate,
    /// and the plates cross over: the separate backgrounds fade as the single one arrives.
    /// The items set off one after another rather than together, which is what makes four
    /// things look like they run together instead of being resized, and the positions
    /// overshoot slightly on arrival — the difference between a droplet landing and a
    /// rectangle being moved.
    /// </remarks>
    private static void MorphFrame(
        DrawingContext context,
        (string Value, string Kind)[] segments,
        IReadOnlyList<Rect> slots,
        Rect merged,
        Rect region,
        double progress,
        bool dark)
    {
        // The merged plate fades in over the second half, so it appears as the items meet
        // rather than waiting there with nothing in it.
        var plateAlpha = Math.Clamp((progress - 0.30) / 0.55, 0, 1);
        if (plateAlpha > 0)
        {
            var fill = new SolidColorBrush(dark
                ? Color.FromArgb((byte)(0xEE * plateAlpha), 0x14, 0x1C, 0x26)
                : Color.FromArgb((byte)(0xF5 * plateAlpha), 0xFF, 0xFF, 0xFF));
            var edge = new Pen(new SolidColorBrush(dark
                ? Color.FromArgb((byte)(0x33 * plateAlpha), 0xFF, 0xFF, 0xFF)
                : Color.FromArgb((byte)(0x22 * plateAlpha), 0x0F, 0x17, 0x2A)), 1);
            context.DrawRoundedRectangle(fill, edge, Offset(merged, region), 14, 14);
        }

        var sizes = segments
            .Select((segment, index) => Text(segment.Value, 13, dark ? Paper : Ink, semibold: index == 0))
            .ToArray();
        var needed = sizes.Sum(text => 12 + text.Width) + 24 * (segments.Length - 1);
        var start = merged.Left + Math.Max(16, (merged.Width - needed) / 2);

        for (var index = 0; index < segments.Length; index++)
        {
            var stagger = index * 0.07;
            var local = Math.Clamp((progress - stagger) / (1 - stagger), 0, 1);
            var eased = EaseOutBack(local);

            var slot = slots[index];
            var from = new Rect(slot.Left + (slot.Width - 196) / 2, slot.Top + (slot.Height - 46) / 2, 196, 46);
            var target = start + sizes.Take(index).Sum(text => 12 + text.Width + 24);
            var to = new Rect(target - 2, merged.Top + 3, sizes[index].Width + 16, merged.Height - 6);
            var rect = Lerp(from, to, eased);

            var own = 1 - Math.Clamp(local * 1.15, 0, 1);
            if (own > 0.02)
            {
                var fill = new SolidColorBrush(dark
                    ? Color.FromArgb((byte)(0xEE * own), 0x14, 0x1C, 0x26)
                    : Color.FromArgb((byte)(0xF5 * own), 0xFF, 0xFF, 0xFF));
                var edge = new Pen(new SolidColorBrush(dark
                    ? Color.FromArgb((byte)(0x33 * own), 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb((byte)(0x22 * own), 0x0F, 0x17, 0x2A)), 1);
                context.DrawRoundedRectangle(fill, edge, Offset(rect, region), 12, 12);
            }

            context.DrawRoundedRectangle(
                new SolidColorBrush(Accent(segments[index].Kind, dark)), null,
                Offset(new Rect(rect.Left + 12, rect.Top + rect.Height / 2 - 3, 6, 6), region), 3, 3);
            var text = Offset(new Rect(rect.Left + 24, rect.Top + (rect.Height - sizes[index].Height) / 2, 0, 0), region);
            context.DrawText(sizes[index], new Point(text.Left, text.Top));
        }
    }

    private static Rect Offset(Rect rect, Rect region)
        => new(rect.Left - region.Left, rect.Top - region.Top, rect.Width, rect.Height);

    private static Rect Lerp(Rect from, Rect to, double t)
        => new(
            from.Left + (to.Left - from.Left) * t,
            from.Top + (to.Top - from.Top) * t,
            from.Width + (to.Width - from.Width) * t,
            from.Height + (to.Height - from.Height) * t);

    /// <summary>Overshoots a little before settling: a droplet landing, not a slide.</summary>
    private static double EaseOutBack(double t)
    {
        const double c1 = 1.20;
        const double c3 = c1 + 1;
        var p = t - 1;
        return 1 + c3 * p * p * p + c1 * p * p;
    }

    private static Color Accent(string kind, bool dark) => kind switch
    {
        "balance" => dark ? Color.FromRgb(0x2D, 0xE1, 0xC2) : Color.FromRgb(0x07, 0x8C, 0x82),
        "task" => dark ? Color.FromRgb(0x78, 0xA9, 0xFF) : Color.FromRgb(0x34, 0x4F, 0x91),
        "account" => dark ? Color.FromRgb(0xFB, 0xBF, 0x24) : Color.FromRgb(0xB4, 0x53, 0x09),
        _ => dark ? Color.FromRgb(0x94, 0xA3, 0xB8) : Color.FromRgb(0x47, 0x55, 0x69)
    };

    private static FormattedText Text(string value, double size, Color colour, bool semibold = false)
        => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                semibold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(colour), 1.0);
}
