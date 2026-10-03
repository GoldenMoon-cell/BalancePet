using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BalancePet.NotificationCenter;

/// <summary>
/// A sketch of the message centre window, redrawn in the language the hover plates use.
/// </summary>
/// <remarks>
/// The window is being rebuilt around the changelog, and the question being asked is
/// whether it belongs to the pet. So the sketch uses the plate vocabulary already agreed
/// for the ring — translucent rounded surfaces, one accent per kind, values rather than
/// borders — carries the pet itself in the title bar, and is drawn at the window's real
/// size over the wallpaper, because a window that composites against the desktop cannot be
/// judged on a white page.
/// </remarks>
internal static class WindowSketch
{
    private const double Width = 820;
    private const double Height = 600;
    private const double Radius = 12;

    public static int Run(string outputPath, bool dark, string? petImagePath)
    {
        var workArea = new Rect(0, 0, Math.Max(1280, SystemParameters.WorkArea.Width), Math.Max(820, SystemParameters.WorkArea.Height));
        // Centred, the way it opens, and large enough around it to show what it is sitting on.
        var window = new Rect(
            (workArea.Width - Width) / 2, (workArea.Height - Height) / 2, Width, Height);

        var backdrop = new RenderTargetBitmap(
            (int)Math.Round(workArea.Width), (int)Math.Round(workArea.Height), 96, 96, PixelFormats.Pbgra32);
        var painting = new DrawingVisual();
        using (var context = painting.RenderOpen())
            PreviewRenderer.DrawWallpaper(context, new Rect(0, 0, workArea.Width, workArea.Height));
        backdrop.Render(painting);

        var region = Rect.Intersect(
            new Rect(window.Left - 90, window.Top - 70, window.Width + 180, window.Height + 140), workArea);
        var composed = new RenderTargetBitmap(
            (int)Math.Round(region.Width), (int)Math.Round(region.Height), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(backdrop, new Rect(-region.Left, -region.Top, workArea.Width, workArea.Height));
            DrawWindow(context, new Rect(window.Left - region.Left, window.Top - region.Top, Width, Height), dark, petImagePath);
        }
        composed.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var stream = System.IO.File.Create(outputPath);
        encoder.Save(stream);
        Console.WriteLine($"窗口草图已写出 {outputPath}  {composed.PixelWidth}×{composed.PixelHeight}  {(dark ? "深色" : "浅色")}");
        return 0;
    }

    private static void DrawWindow(DrawingContext context, Rect window, bool dark, string? petImagePath)
    {
        var paper = dark ? Color.FromRgb(0x11, 0x17, 0x1F) : Color.FromRgb(0xFA, 0xFC, 0xFC);
        var plate = dark ? Color.FromArgb(0xCC, 0x1B, 0x24, 0x30) : Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF);
        var ink = dark ? Color.FromRgb(0xF1, 0xF5, 0xF9) : Color.FromRgb(0x0F, 0x17, 0x2A);
        var muted = dark ? Color.FromRgb(0x9F, 0xB0, 0xC2) : Color.FromRgb(0x5A, 0x6B, 0x7B);
        var accent = dark ? Color.FromRgb(0x2D, 0xE1, 0xC2) : Color.FromRgb(0x07, 0x8C, 0x82);

        // Softness rather than a border: three passes of decreasing alpha stand in for the
        // shadow a real window gets from the compositor.
        for (var pass = 3; pass >= 1; pass--)
        {
            var spread = pass * 7.0;
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb((byte)(0x0E / pass), 0, 0, 0)), null,
                new Rect(window.Left - spread, window.Top - spread + 4, window.Width + spread * 2, window.Height + spread * 2),
                Radius + spread, Radius + spread);
        }

        // The window itself, translucent: this is what it looks like over a desktop.
        context.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0xF0, paper.R, paper.G, paper.B)),
            new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x0F, 0x17, 0x2A)), 1),
            window, Radius, Radius);

        DrawTitleBar(context, window, dark, petImagePath, ink, muted, accent, paper);
        DrawChangelog(context, window, dark, ink, muted, accent, plate);
        DrawFooter(context, window, dark, ink, muted, accent);
    }

    private static void DrawTitleBar(
        DrawingContext context, Rect window, bool dark, string? petImagePath,
        Color ink, Color muted, Color accent, Color paper)
    {
        const double bar = 52;
        // The pet, in its own window: a round portrait cropped to the head, which is the
        // one thing that makes the window unmistakably this program's.
        var avatar = new Rect(window.Left + 18, window.Top + 10, 34, 34);
        context.DrawEllipse(new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x0F, 0x17, 0x2A)), null, 
            new Point(avatar.Left + avatar.Width / 2, avatar.Top + avatar.Height / 2), avatar.Width / 2, avatar.Height / 2);
        DrawAvatar(context, avatar, petImagePath);

        var title = Text("消息中心", 15, ink, semibold: true);
        context.DrawText(title, new Point(avatar.Right + 12, window.Top + 13));
        var subtitle = Text("更新记录与桌面提示", 11.5, muted);
        context.DrawText(subtitle, new Point(avatar.Right + 12, window.Top + 33));

        // Caption buttons, drawn plain: the window's own chrome is not what is being decided.
        var close = new Rect(window.Right - 46, window.Top, 46, bar);
        for (var index = 0; index < 3; index++)
        {
            var x = window.Right - 46 - index * 46;
            var icon = index switch { 0 => "—", 1 => "▢", _ => "✕" };
            var glyph = Text(icon, 12, muted);
            context.DrawText(glyph, new Point(x + (46 - glyph.Width) / 2, window.Top + 18));
        }
        context.DrawRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x0F, 0x17, 0x2A)),
            null, new Rect(0, 0, 0, 0));
        context.DrawRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x0F, 0x17, 0x2A)),
            null, new Rect(window.Left, window.Top + bar, window.Width, 1));
    }

    private static void DrawAvatar(DrawingContext context, Rect avatar, string? petImagePath)
    {
        var path = petImagePath ?? PreviewRenderer.FindInstalledPetArtwork();
        if (path is null || !System.IO.File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            context.PushClip(new EllipseGeometry(new Point(avatar.Left + avatar.Width / 2, avatar.Top + avatar.Height / 2), avatar.Width / 2, avatar.Height / 2));
            // The head, not the whole figure: the artwork is a full body and a portrait wants
            // the top of it.
            var head = new Rect(avatar.Left - 10, avatar.Top - 26, avatar.Width + 20, avatar.Height + 34);
            context.DrawImage(image, head);
            context.Pop();
        }
        catch (Exception)
        {
        }
    }

    private static void DrawChangelog(
        DrawingContext context, Rect window, bool dark, Color ink, Color muted, Color accent, Color plate)
    {
        var heading = Text("更新记录", 16, ink, semibold: true);
        context.DrawText(heading, new Point(window.Left + 24, window.Top + 74));
        var count = Text("共 6 条 · 由桌宠自动获取", 11.5, muted);
        context.DrawText(count, new Point(window.Left + 24 + heading.Width + 10, window.Top + 79));

        var entries = new (string Title, string Area, string Date, string Summary)[]
        {
            ("形象预览与介绍", "在线内容", "2026-10-03", "在线插件库的每个形象条目补上了一句话介绍，并新增可选的 icon_url 指向仓库里的缩影图。"),
            ("新增两套形象", "在线内容", "2026-10-02", "形象仓库新增 OpenCode 小码灵「墨枢」与 Perplexity 小探灯「青鉴」，各九状态与专属台词均已就位。"),
            ("通告文档有了自己的规范", "规范", "2026-10-02", "更新记录读取的 notices.json 此前只有读取端、没有对外规范，现已补上说明与 schema。"),
            ("补充形象仓库文档规范", "规范", "2026-10-02", "形象目录 catalog.json 与在线台词 lines.json 此前只有实现、没有规范，现已补齐两份 schema。")
        };

        var top = window.Top + 108;
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var rect = new Rect(window.Left + 20, top + index * 92, window.Width - 40, 80);
            context.DrawRoundedRectangle(new SolidColorBrush(plate),
                new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x0F, 0x17, 0x2A)), 1),
                rect, 12, 12);

            // One accent per kind, as a bar down the leading edge: the same mark the hover
            // plates use, so the two surfaces are visibly the same family.
            context.DrawRoundedRectangle(new SolidColorBrush(accent), null,
                new Rect(rect.Left + 12, rect.Top + 16, 3, rect.Height - 32), 1.5, 1.5);

            context.DrawText(Text(entry.Title, 14.5, ink, semibold: true), new Point(rect.Left + 26, rect.Top + 14));
            var chip = Text(entry.Area, 11, accent);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x22 : (byte)0x16, accent.R, accent.G, accent.B)),
                null, new Rect(rect.Left + 26 + Text(entry.Title, 14.5, ink, semibold: true).Width + 10, rect.Top + 15, chip.Width + 14, 18), 9, 9);
            context.DrawText(chip, new Point(rect.Left + 33 + Text(entry.Title, 14.5, ink, semibold: true).Width + 10, rect.Top + 17));

            var date = Text(entry.Date, 11.5, muted);
            context.DrawText(date, new Point(rect.Right - 16 - date.Width, rect.Top + 17));

            var summary = Text(entry.Summary, 12.5, muted);
            summary.MaxTextWidth = rect.Width - 140;
            summary.Trimming = TextTrimming.CharacterEllipsis;
            summary.MaxLineCount = 1;
            context.DrawText(summary, new Point(rect.Left + 26, rect.Top + 42));

            // The one action an entry has: open where it points.
            var open = Text("打开", 12, dark ? accent : accent);
            var button = new Rect(rect.Right - 16 - (open.Width + 26), rect.Bottom - 30, open.Width + 26, 22);
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(dark ? (byte)0x26 : (byte)0x1C, accent.R, accent.G, accent.B)),
                null, button, 11, 11);
            context.DrawText(open, new Point(button.Left + 13, button.Top + 4));
        }
    }

    private static void DrawFooter(DrawingContext context, Rect window, bool dark, Color ink, Color muted, Color accent)
    {
        var bar = new Rect(window.Left, window.Bottom - 56, window.Width, 56);
        context.DrawRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x20, 0x0A, 0x0E, 0x14) : Color.FromArgb(0xE8, 0xF2, 0xF7, 0xF7)), null, bar);

        // The switch the user asked for: take over the pet's own bubbles, or leave them
        // alone. On by default, because that is what the extension is for.
        var toggle = new Rect(bar.Left + 24, bar.Top + 18, 36, 20);
        context.DrawRoundedRectangle(new SolidColorBrush(accent), null, toggle, 10, 10);
        context.DrawEllipse(new SolidColorBrush(Colors.White), null, new Point(toggle.Right - 10, toggle.Top + toggle.Height / 2), 7, 7);
        context.DrawText(Text("接管气泡提醒", 12.5, ink), new Point(toggle.Right + 10, bar.Top + 20));

        var status = Text("本地保存 · 自动跟随桌宠 · 关闭窗口后仍在后台", 11.5, muted);
        context.DrawText(status, new Point(bar.Right - 24 - status.Width, bar.Top + 22));
    }

    private static FormattedText Text(string value, double size, Color colour, bool semibold = false)
        => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                semibold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(colour), 1.0);
}
