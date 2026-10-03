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

    /// <param name="section">
    /// Which section to draw. Two are drawn across the sketches because the layout is the
    /// question being asked: this window will not only show the changelog, so the rail and
    /// the row have to hold a version notice or a balance change without looking like they
    /// were built for something else.
    /// </param>
    public static int Run(string outputPath, bool dark, string? petImagePath, string section = "notice")
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
            DrawWindow(context, new Rect(window.Left - region.Left, window.Top - region.Top, Width, Height), dark, petImagePath, section);
        }
        composed.Render(drawing);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var stream = System.IO.File.Create(outputPath);
        encoder.Save(stream);
        Console.WriteLine($"窗口草图已写出 {outputPath}  {composed.PixelWidth}×{composed.PixelHeight}  {(dark ? "深色" : "浅色")}");
        return 0;
    }

    private static void DrawWindow(DrawingContext context, Rect window, bool dark, string? petImagePath, string section)
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
        // A rail rather than a row of filter chips along the top: the sections are going to
        // grow, and this is the shape the settings window already uses, so the two windows
        // read as one program.
        DrawRail(context, window, dark, ink, muted, accent, section);
        DrawMessages(context, window, dark, ink, muted, accent, plate, section);
        DrawFooter(context, window, dark, ink, muted, accent);
    }

    /// <summary>
    /// The sections, which is the part that has to outlive a window whose only content was
    /// the changelog.
    /// </summary>
    private static void DrawRail(
        DrawingContext context, Rect window, bool dark, Color ink, Color muted, Color accent, string section)
    {
        const double width = 186;
        var rail = new Rect(window.Left, window.Top + 52, width, window.Height - 52);
        context.DrawRectangle(
            new SolidColorBrush(dark ? Color.FromArgb(0x2E, 0x0A, 0x0E, 0x14) : Color.FromArgb(0x8C, 0xF1, 0xF6, 0xF6)),
            null, rail);
        context.DrawRectangle(
            new SolidColorBrush(dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x0F, 0x17, 0x2A)),
            null, new Rect(rail.Right, rail.Top, 1, rail.Height));

        // 更新记录 is real; the rest are there to show what the layout does with them.
        var sections = new (string Name, string Key, string Count)[]
        {
            ("更新记录", "notice", "6"), ("系统通知", "system", "2"), ("扩展", "extension", ""), ("余额记录", "balance", "")
        };
        var top = rail.Top + 14;
        foreach (var item in sections)
        {
            var selected = item.Key == section || (section == "all" && item.Key == "notice");
            var row = new Rect(rail.Left + 10, top, width - 20, 38);
            if (selected)
            {
                context.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(dark ? (byte)0x2E : (byte)0x1C, accent.R, accent.G, accent.B)),
                    null, row, 9, 9);
                context.DrawRoundedRectangle(new SolidColorBrush(accent), null,
                    new Rect(row.Left + 2, row.Top + 10, 2.5, 18), 1.5, 1.5);
            }
            context.DrawText(Text(item.Name, 13, selected ? ink : muted, semibold: selected),
                new Point(row.Left + 16, row.Top + 10));
            if (item.Count.Length > 0)
            {
                var badge = Text(item.Count, 10.5, selected ? accent : muted);
                context.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(dark ? (byte)0x22 : (byte)0x18, accent.R, accent.G, accent.B)),
                    null, new Rect(row.Right - badge.Width - 22, row.Top + 11, badge.Width + 14, 17), 8.5, 8.5);
                context.DrawText(badge, new Point(row.Right - badge.Width - 15, row.Top + 13));
            }
            top += 42;
        }

        // The takeover switch is a setting, so it lives here rather than in the footer,
        // which is for saying what the window is doing.
        context.DrawText(Text("设置", 13, muted), new Point(rail.Left + 26, rail.Bottom - 116));
        var toggle = new Rect(rail.Left + 26, rail.Bottom - 90, 34, 19);
        context.DrawRoundedRectangle(new SolidColorBrush(accent), null, toggle, 9.5, 9.5);
        context.DrawEllipse(new SolidColorBrush(Colors.White), null,
            new Point(toggle.Right - 9.5, toggle.Top + 9.5), 6.5, 6.5);
        context.DrawText(Text("接管气泡提醒", 12, ink), new Point(toggle.Right + 9, rail.Bottom - 89));
    }

    /// <summary>
    /// The message rows, drawn from one description whatever the message is about.
    /// </summary>
    /// <remarks>
    /// A row is an accent, a title, an optional tag, a date, a line of body and at most one
    /// action. A changelog entry, a version notice and a balance change all fit that, which
    /// is the point: a layout built around a single kind of message has to be thrown away
    /// when the second kind arrives — and the second kind is coming.
    /// </remarks>
    private static void DrawMessages(
        DrawingContext context, Rect window, bool dark, Color ink, Color muted, Color accent, Color plate, string section)
    {
        var contentLeft = window.Left + 186 + 24;
        var contentWidth = window.Right - 24 - contentLeft;

        var heading = Text(section == "all" ? "全部消息" : "更新记录", 16, ink, semibold: true);
        context.DrawText(heading, new Point(contentLeft, window.Top + 74));
        var subtitle = Text(section == "all" ? "按时间排列 · 桌面提示与更新记录" : "共 6 条 · 由桌宠自动获取", 11.5, muted);
        context.DrawText(subtitle, new Point(contentLeft + heading.Width + 10, window.Top + 79));

        var rows = section == "all"
            ? new (string Title, string Tag, string Date, string Body, string? Action, string Kind)[]
            {
                ("形象预览与介绍", "在线内容", "2026-10-03", "在线插件库的每个形象条目补上了一句话介绍，并新增可选的 icon_url 指向仓库里的缩影图。", "打开", "notice"),
                ("有新的版本可以安装", "版本", "2026-10-03", "BalancePet 1.5.1 已发布：修复设置面板的崩溃，更新检查不再被限流卡住。", "查看", "update"),
                ("本次消耗 7.70 CNY", "余额", "2026-10-03", "DeepSeek · 账户余额 42.80 CNY", null, "balance"),
                ("扩展「用量统计」更新完成", "扩展", "2026-10-02", "0.4.0 → 0.5.0，新增按模型统计。", null, "extension"),
                ("查询失败", "刷新", "2026-10-02", "余额接口暂时不可用，已自动重试。", null, "refresh")
            }
            : new (string Title, string Tag, string Date, string Body, string? Action, string Kind)[]
            {
                ("形象预览与介绍", "在线内容", "2026-10-03", "在线插件库的每个形象条目补上了一句话介绍，并新增可选的 icon_url 指向仓库里的缩影图。", "打开", "notice"),
                ("新增两套形象", "在线内容", "2026-10-02", "形象仓库新增 OpenCode 小码灵「墨枢」与 Perplexity 小探灯「青鉴」，各九状态与专属台词均已就位。", "打开", "notice"),
                ("通告文档有了自己的规范", "规范", "2026-10-02", "更新记录读取的 notices.json 此前只有读取端、没有对外规范，现已补上说明与 schema。", "打开", "notice"),
                ("补充形象仓库文档规范", "规范", "2026-10-02", "形象目录 catalog.json 与在线台词 lines.json 此前只有实现、没有规范，现已补齐两份 schema。", "打开", "notice")
            };

        var top = window.Top + 106;
        foreach (var row in rows)
        {
            var rect = new Rect(contentLeft, top, contentWidth, 78);
            context.DrawRoundedRectangle(new SolidColorBrush(plate),
                new Pen(new SolidColorBrush(dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0x0F, 0x17, 0x2A)), 1),
                rect, 12, 12);

            // One accent per kind, as a bar down the leading edge: the same mark the hover
            // plates use, so the two surfaces are visibly the same family.
            var kind = Accent(row.Kind, dark, accent);
            context.DrawRoundedRectangle(new SolidColorBrush(kind), null,
                new Rect(rect.Left + 12, rect.Top + 16, 3, rect.Height - 32), 1.5, 1.5);

            var title = Text(row.Title, 14.5, ink, semibold: true);
            context.DrawText(title, new Point(rect.Left + 26, rect.Top + 13));
            if (row.Tag.Length > 0)
            {
                var chip = Text(row.Tag, 11, kind);
                var chipLeft = rect.Left + 26 + title.Width + 10;
                context.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(dark ? (byte)0x24 : (byte)0x16, kind.R, kind.G, kind.B)),
                    null, new Rect(chipLeft, rect.Top + 14, chip.Width + 14, 18), 9, 9);
                context.DrawText(chip, new Point(chipLeft + 7, rect.Top + 16));
            }

            var date = Text(row.Date, 11.5, muted);
            context.DrawText(date, new Point(rect.Right - 16 - date.Width, rect.Top + 16));

            var body = Text(row.Body, 12.5, muted);
            body.MaxTextWidth = rect.Width - (row.Action is null ? 52 : 130);
            body.Trimming = TextTrimming.CharacterEllipsis;
            body.MaxLineCount = 1;
            context.DrawText(body, new Point(rect.Left + 26, rect.Top + 40));

            if (row.Action is not null)
            {
                var action = Text(row.Action, 12, kind);
                var button = new Rect(rect.Right - 16 - (action.Width + 26), rect.Bottom - 30, action.Width + 26, 22);
                context.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(dark ? (byte)0x26 : (byte)0x1C, kind.R, kind.G, kind.B)),
                    null, button, 11, 11);
                context.DrawText(action, new Point(button.Left + 13, button.Top + 4));
            }

            top += 88;
        }
    }

    /// <summary>The colour a kind of message is marked with, matching the hover plates.</summary>
    private static Color Accent(string kind, bool dark, Color fallback) => kind switch
    {
        "balance" => dark ? Color.FromRgb(0x2D, 0xE1, 0xC2) : Color.FromRgb(0x07, 0x8C, 0x82),
        "task" or "update" => dark ? Color.FromRgb(0x78, 0xA9, 0xFF) : Color.FromRgb(0x34, 0x4F, 0x91),
        "extension" => dark ? Color.FromRgb(0xC4, 0xB5, 0xFD) : Color.FromRgb(0x6D, 0x28, 0xD9),
        "refresh" => dark ? Color.FromRgb(0xFB, 0xBF, 0x24) : Color.FromRgb(0xB4, 0x53, 0x09),
        _ => fallback
    };

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

    private static void DrawFooter(DrawingContext context, Rect window, bool dark, Color ink, Color muted, Color accent)
    {
        var bar = new Rect(window.Left, window.Bottom - 46, window.Width, 46);
        context.DrawRectangle(new SolidColorBrush(dark ? Color.FromArgb(0x20, 0x0A, 0x0E, 0x14) : Color.FromArgb(0xD8, 0xF2, 0xF7, 0xF7)), null, bar);

        // The takeover switch moved into the rail, where the settings are: this bar says
        // what the window is doing rather than changing it.
        var status = Text("本地保存 · 关闭窗口后继续在后台 · 由桌宠自动获取", 11.5, muted);
        context.DrawText(status, new Point(bar.Left + 24, bar.Top + 14));
    }

    private static FormattedText Text(string value, double size, Color colour, bool semibold = false)
        => new(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                semibold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(colour), 1.0);
}
