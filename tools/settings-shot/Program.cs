using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BalancePet.Wpf;
using BalancePet.Wpf.Services;

namespace BalancePet.SettingsShot;

/// <summary>
/// Renders the real settings window to a PNG so a layout change can be looked at
/// rather than only compiled.
///
/// The window is built exactly as the application builds it, from the user's own
/// settings file, and then placed far off any monitor. It lays out and renders
/// normally there, so the capture is of the genuine window and not of a replica —
/// while nothing appears in front of whatever the user is doing.
///
/// The settings window only reads. The changelog window writes: opening it records that
/// its entries have been read, exactly as it does in the application. Point
/// BALANCEPET_CSHARP_CONFIG at a scratch file before capturing that one, or the capture
/// will advance the user's own watermark.
///
/// Scratch output goes beside the binary rather than in %TEMP%. The workspace
/// carries a low mandatory label, so a process started from it inherits a
/// low-integrity token and is refused when it writes to a medium-integrity
/// location such as the temporary directory.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // A screen recording of the UI is only useful if its frames can be looked at one at a
        // time, and there is no video tool on this machine to do it with.
        if (Array.IndexOf(args, "--video-frames") is var vt && vt >= 0 && vt + 1 < args.Length)
        {
            var howMany = Array.IndexOf(args, "--frames") is var fc && fc >= 0 && fc + 1 < args.Length
                && int.TryParse(args[fc + 1], out var parsed) ? parsed : 8;
            return SaveVideoFrames(args[vt + 1], howMany);
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var store = new SettingsStore();
        var settings = store.Load();
        // An appearance can be forced on the command line so a state that only some
        // appearances have -- an animated one, or one with no artwork at all -- can be
        // captured without editing the user's settings file.
        if (Array.IndexOf(args, "--style") is var at && at >= 0 && at + 1 < args.Length)
            settings.PetStyle = args[at + 1];
        // The page to capture, named by any element it contains.
        var tabElement = Array.IndexOf(args, "--tab") is var tt && tt >= 0 && tt + 1 < args.Length
            ? args[tt + 1]
            : "PetPreviewGrid";
        // The catalogs are fetched asynchronously while the window opens, so a capture
        // taken immediately shows the page mid-load. --wait pumps the dispatcher until
        // the answers have been applied.
        var waitSeconds = Array.IndexOf(args, "--wait") is var wt && wt >= 0 && wt + 1 < args.Length && int.TryParse(args[wt + 1], out var seconds)
            ? seconds
            : 0;
        // Timed because the window is built on the UI thread: whatever the constructor
        // spends is time the user spends looking at nothing after clicking 设置.
        // A document to show instead of whatever the cache holds. Without it the changelog
        // window renders empty here, and the populated layout -- long titles, summaries,
        // the new-entry badge, the per-row buttons -- is the one worth looking at.
        if (Array.IndexOf(args, "--notices") is var nt && nt >= 0 && nt + 1 < args.Length && File.Exists(args[nt + 1]))
            NoticeFeed.Publish(File.ReadAllText(args[nt + 1]));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // Which window to capture. The changelog is built here too rather than only in the
        // application, because a window that has never been rendered is a window nobody has
        // looked at -- and the alternative is discovering a clipped label in a release.
        var asNotice = Array.IndexOf(args, "--window") is var wt2 && wt2 >= 0 && wt2 + 1 < args.Length
            && string.Equals(args[wt2 + 1], "notice", StringComparison.OrdinalIgnoreCase);
        Window window = asNotice
            ? new NoticeWindow(store, settings, new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            : new SettingsWindow(store, new DpapiTokenStore(), settings);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowInTaskbar = false;
        window.Topmost = false;
        stopwatch.Stop();
        Console.WriteLine($"构造耗时 {stopwatch.ElapsedMilliseconds} ms");

        try
        {
            stopwatch.Restart();
            window.Show();
            window.UpdateLayout();
            stopwatch.Stop();
            Console.WriteLine($"首次布局耗时 {stopwatch.ElapsedMilliseconds} ms");
            SelectTabContaining(window, tabElement);            // Selecting a tab builds its content, and a tab nested inside it builds
            // its own content one pass later, so a single UpdateLayout can capture a
            // half-built page. Draining the dispatcher lets every pass finish.
            for (var pass = 0; pass < 4; pass++)
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.UpdateLayout();
            }

            // Pumping on the UI thread rather than awaiting: the window's own load
            // continuations are posted to this dispatcher, so they only run while it
            // is being drained.
            var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Thread.Sleep(120);
            }
            window.UpdateLayout();

            // A ComboBox popup lives in its own window, so RenderTargetBitmap cannot capture
            // it however far off-screen the owner is. Measuring it is the alternative: the
            // digits say whether a highlight sits inside the popup, which is the question a
            // screenshot of a clipped corner would have been asked to answer anyway.
            if (Array.IndexOf(args, "--combo") is var ct && ct >= 0 && ct + 1 < args.Length)
                return ReportPopupLayout(window, args[ct + 1]);

            if (Array.IndexOf(args, "--bounds") is var bt && bt >= 0 && bt + 1 < args.Length)
                return ReportBounds(window, args[bt + 1]);

            if (Array.IndexOf(args, "--type") is var tt2 && tt2 >= 0 && tt2 + 1 < args.Length)
                return ReportType(window, args[tt2 + 1]);

            // Renders the real window in a candidate font. The window's own font does not
            // cascade to its children, because the shared styles set FontFamily explicitly, so
            // every text-bearing element is set individually. That is the point: a font has to
            // be judged on the labels it will actually be used for, at the sizes they are
            // actually drawn at, not on a specimen line.
            if (Array.IndexOf(args, "--font") is var ft && ft >= 0 && ft + 1 < args.Length)
            {
                // Either an installed family name, or "<file>|<family>" to render straight out
                // of a downloaded file. The second form is what makes a candidate judgeable
                // before anyone decides to install it.
                var spec = args[ft + 1];
                var bar = spec.IndexOf('|');
                var family = bar > 0
                    ? new System.Windows.Media.FontFamily(
                        new Uri("file:///" + System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(spec[..bar]))!.Replace('\\', '/') + "/"),
                        "./#" + spec[(bar + 1)..])
                    : new System.Windows.Media.FontFamily(spec);

                foreach (var element in EnumerateAll(window))
                {
                    switch (element)
                    {
                        case System.Windows.Controls.TextBlock text: text.FontFamily = family; break;
                        case System.Windows.Controls.Control control: control.FontFamily = family; break;
                    }
                }
                for (var pass = 0; pass < 3; pass++)
                {
                    window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    window.UpdateLayout();
                }
            }

            var width = (int)Math.Ceiling(window.ActualWidth);
            var height = (int)Math.Ceiling(window.ActualHeight);            if (width <= 0 || height <= 0)
            {
                Console.WriteLine("窗口没有布局尺寸，无法截图。");
                return 2;
            }

            var dpi = Array.IndexOf(args, "--dpi") is var dt && dt >= 0 && dt + 1 < args.Length && double.TryParse(args[dt + 1], out var parsedDpi)
                ? parsedDpi
                : 96d;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var target = Path.Combine(AppContext.BaseDirectory, "settings-shot.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(target)) encoder.Save(stream);

            Console.WriteLine($"{target}  {width}x{height}");
            return 0;
        }
        finally
        {
            // Deliberately not Close(): closing runs the window's own save-on-exit
            // path, and this tool must not write to the user's settings.
            window.Hide();
            application.Shutdown();
        }
    }

    /// <summary>
    /// Opens a named ComboBox and reports where its items actually landed.
    /// </summary>
    /// <remarks>
    /// Prints the popup's inner rectangle and each item's, in the popup's own coordinate
    /// space, and flags any item that does not fit. An item wider than the space it has, or
    /// flush against another item, is what makes a rounded highlight read as cut off.
    /// </remarks>
    /// <summary>
    /// Saves still frames from a video, so a screen recording can be read like a screenshot.
    /// </summary>
    /// <remarks>
    /// MediaPlayer plus RenderTargetBitmap rather than a decoder dependency: WPF already has
    /// one. Scrubbing has to be on and the seek needs a moment to land, or every frame comes
    /// back as the first one.
    /// </remarks>
    private static int SaveVideoFrames(string path, int count)
    {
        var player = new System.Windows.Media.MediaPlayer { ScrubbingEnabled = true, Volume = 0 };
        var opened = false;
        player.MediaOpened += (_, _) => opened = true;
        player.MediaFailed += (_, e) => Console.WriteLine($"open failed: {e.ErrorException?.Message}");
        player.Open(new Uri(Path.GetFullPath(path)));
        player.Play();
        player.Pause();

        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        for (var spin = 0; spin < 300 && !opened; spin++)
        {
            dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(20);
        }
        if (!opened) { Console.WriteLine("video did not open"); return 2; }

        var duration = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        Console.WriteLine($"duration {duration.TotalSeconds:0.##}s  {player.NaturalVideoWidth}x{player.NaturalVideoHeight}");
        if (duration <= TimeSpan.Zero) return 2;

        for (var index = 0; index < count; index++)
        {
            var at = TimeSpan.FromTicks(duration.Ticks * (index * 2 + 1) / (count * 2));
            player.Position = at;
            for (var spin = 0; spin < 50; spin++)
            {
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Thread.Sleep(20);
            }

            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawVideo(player, new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight));
            var bitmap = new RenderTargetBitmap(player.NaturalVideoWidth, player.NaturalVideoHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var target = Path.Combine(AppContext.BaseDirectory, $"frame-{index:00}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(target)) encoder.Save(stream);
            Console.WriteLine($"  {at.TotalSeconds,6:0.##}s  {target}");
        }
        player.Close();
        return 0;
    }

    private static int ReportPopupLayout(FrameworkElement window, string comboName)
    {
        if (window.FindName(comboName) is not System.Windows.Controls.ComboBox combo)
        {
            Console.WriteLine($"找不到 ComboBox：{comboName}");
            return 2;
        }

        combo.IsDropDownOpen = true;
        for (var pass = 0; pass < 4; pass++)
        {
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            window.UpdateLayout();
        }

        var popup = FindDescendant<System.Windows.Controls.Primitives.Popup>(combo);
        if (popup?.Child is not FrameworkElement chrome)
        {
            Console.WriteLine("下拉没有打开或找不到弹出层。");
            return 2;
        }

        // A Popup renders in its own window, so RenderTargetBitmap cannot reach it and the
        // whole dropdown has been unphotographable. Its Child can be rendered on its own,
        // which is enough to see what the list actually looks like.
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--popup-shot") is var ps && ps >= 0)
        {
            // Pumped, not just UpdateLayout(). A popup lays itself out on the dispatcher, and
            // rendering before that finishes captures a half-sized thumb, which reads exactly
            // like the clipping this is meant to be checking for.
            for (var pass = 0; pass < 4; pass++)
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                chrome.UpdateLayout();
            }
            var shot = new RenderTargetBitmap(
                (int)Math.Ceiling(chrome.ActualWidth), (int)Math.Ceiling(chrome.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            shot.Render(chrome);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(shot));
            var popupPath = Path.Combine(AppContext.BaseDirectory, "settings-popup.png");
            using (var stream = File.Create(popupPath)) png.Save(stream);
            Console.WriteLine($"弹出层截图 {popupPath}  {(int)chrome.ActualWidth}x{(int)chrome.ActualHeight}");
        }

        var padding = chrome is System.Windows.Controls.Border { Padding: var p } ? p : default;
        Console.WriteLine($"弹出层 {chrome.ActualWidth:0.##} x {chrome.ActualHeight:0.##}，内边距 L{padding.Left} T{padding.Top} R{padding.Right} B{padding.Bottom}");
        Console.WriteLine($"可用内部宽度 {chrome.ActualWidth - padding.Left - padding.Right:0.##}");
        Console.WriteLine();

        var problems = 0;
        var previousBottom = double.NaN;
        var itemHeights = new List<double>();

        // The scrollbar lives inside the popup, so the window-wide --type walk cannot reach
        // it: a popup renders in its own tree. Reporting it here is the only way to ask a
        // dropdown thumb how tall it is and whether the popup is cutting it off.
        foreach (var bar in FindDescendants<System.Windows.Controls.Primitives.ScrollBar>(chrome))
        {
            var thumb = FindDescendant<System.Windows.Controls.Primitives.Thumb>(bar);
            if (thumb is null)
            {
                Console.WriteLine($"  滚动条 {bar.Orientation} {bar.ActualWidth:0.##} x {bar.ActualHeight:0.##}，没有滑块（{bar.Visibility}）");
                continue;
            }
            var barOrigin = bar.TransformToAncestor(chrome).Transform(new Point(0, 0));
            var thumbOrigin = thumb.TransformToAncestor(chrome).Transform(new Point(0, 0));
            var thumbBottom = thumbOrigin.Y + thumb.ActualHeight;
            var barBottom = barOrigin.Y + bar.ActualHeight;
            var cut = thumbBottom > chrome.ActualHeight + 0.5 || barBottom > chrome.ActualHeight + 0.5;
            if (cut) problems++;
            Console.WriteLine(
                $"  滚动条 {bar.Orientation} 位于 x {barOrigin.X:0.##}..{barOrigin.X + bar.ActualWidth:0.##}，" +
                $"y {barOrigin.Y:0.##}..{barBottom:0.##}（弹出层高 {chrome.ActualHeight:0.##}）");
            Console.WriteLine(
                $"    滑块 高 {thumb.ActualHeight:0.##}  y {thumbOrigin.Y:0.##}..{thumbBottom:0.##}" +
                $"{(cut ? "   <- 底部超出弹出层，会被裁掉" : "")}");

            // The border inside the thumb is the only part with a corner radius, so it is the
            // part that has to be the right size. Reporting the thumb alone says nothing about
            // whether the pill drawn inside it is being clipped.
            if (FindDescendant<System.Windows.Controls.Border>(thumb) is { } pill)
            {
                var pillOrigin = pill.TransformToAncestor(chrome).Transform(new Point(0, 0));
                Console.WriteLine(
                    $"      内层 Border 高 {pill.ActualHeight:0.##}  y {pillOrigin.Y:0.##}..{pillOrigin.Y + pill.ActualHeight:0.##}" +
                    $"  Margin {pill.Margin.Top:0.##}/{pill.Margin.Bottom:0.##}" +
                    $"  CornerRadius {pill.CornerRadius.TopLeft:0.##}");
            }
        }

        foreach (var item in FindDescendants<System.Windows.Controls.ComboBoxItem>(chrome))
        {
            // The highlight is the Border inside the item, not the item itself: the inset
            // that keeps two adjacent highlights apart lives on that Border, so measuring
            // the item reports a flush edge that the user never sees.
            var target = item.Template?.FindName("Chrome", item) as FrameworkElement ?? item;
            var origin = target.TransformToAncestor(chrome).Transform(new Point(0, 0));
            var left = origin.X;
            var right = origin.X + target.ActualWidth;
            var top = origin.Y;
            var bottom = origin.Y + target.ActualHeight;
            var overflowsRight = right > chrome.ActualWidth - padding.Right + 0.5;
            var flush = !double.IsNaN(previousBottom) && Math.Abs(top - previousBottom) < 0.01;
            if (overflowsRight || flush) problems++;
            itemHeights.Add(target.ActualHeight);
            Console.WriteLine(
                $"  {Describe(item),-14} 左 {left,6:0.##}  右 {right,6:0.##}  上 {top,6:0.##}  下 {bottom,6:0.##}" +
                $"{(overflowsRight ? "   ← 超出右边界" : "")}{(flush ? "   ← 与上一项贴合" : "")}");
            previousBottom = bottom;
        }

        Console.WriteLine();
        // Unequal heights are not a defect by themselves, but they are what makes a
        // virtualising list shift under a moving pointer: each item is drawn in its own face,
        // faces load when first shown, and an item that changes height after the panel has
        // measured moves everything below it.
        if (itemHeights.Count > 1)
        {
            var shortest = itemHeights.Min();
            var tallest = itemHeights.Max();
            Console.WriteLine($"条目高度 {shortest:0.##}..{tallest:0.##}（相差 {tallest - shortest:0.##}）");
        }

        Console.WriteLine(problems == 0
            ? "高亮完整落在弹出层内，且相邻高亮之间有空隙。"
            : $"{problems} 处问题：超出右边界或与相邻项贴合。");
        return problems == 0 ? 0 : 1;
    }

    private static string Describe(System.Windows.Controls.ComboBoxItem item)
        => item.Content?.ToString() ?? "(空)";

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        => FindDescendants<T>(root).FirstOrDefault();

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    /// <summary>
    /// Prints every element of a type with its bounds, in window coordinates.
    /// </summary>
    /// <remarks>
    /// For the parts a template never names. A scrollbar thumb is one of those, and reading
    /// its size out of the pixels is guesswork: it is a translucent brush over whatever
    /// happens to be behind it, so there is no colour to look for. Asking the element is
    /// exact.
    /// </remarks>
    private static int ReportType(FrameworkElement window, string typeName)
    {
        var found = 0;
        foreach (var child in EnumerateAll(window))
        {
            if (child.GetType().Name != typeName) continue;
            var origin = child.TransformToAncestor(window).Transform(new Point(0, 0));
            Console.WriteLine($"  {child.GetType().Name,-14} {child.ActualWidth,7:0.##} x {child.ActualHeight,6:0.##}" +
                              $"   位置 {origin.X:0.##},{origin.Y:0.##}   {(child.IsVisible ? "可见" : "不可见")}");
            found++;
        }
        Console.WriteLine();
        Console.WriteLine(found == 0 ? $"没有找到 {typeName}。" : $"共 {found} 个 {typeName}。");
        return 0;
    }

    private static IEnumerable<FrameworkElement> EnumerateAll(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in EnumerateAll(child)) yield return nested;
        }
    }

    /// <summary>
    /// Prints a named element's bounds and every ancestor's, so "it looks cut off" becomes a
    /// comparison of two numbers.
    /// </summary>
    /// <remarks>
    /// A rounded corner is clipped when the element carrying it is wider than the space its
    /// container actually gives it. That is arithmetic rather than a judgement call, but it
    /// is close to invisible in a screenshot: a border three pixels too wide looks like a
    /// border three pixels too wide, which looks fine until the corners turn out to be
    /// missing. This prints the chain so the two numbers can be compared directly.
    /// </remarks>
    private static int ReportBounds(FrameworkElement window, string name)
    {
        if (window.FindName(name) is not FrameworkElement target)
        {
            Console.WriteLine($"找不到元素：{name}");
            return 2;
        }

        var chain = new List<FrameworkElement>();
        DependencyObject? node = target;
        while (node is not null)
        {
            if (node is FrameworkElement element) chain.Add(element);
            var visual = VisualTreeHelper.GetParent(node);
            node = visual ?? (node is FrameworkElement current ? LogicalTreeHelper.GetParent(current) : null);
        }

        var origin = target.TransformToAncestor(window).Transform(new Point(0, 0));
        Console.WriteLine($"{name}: {target.ActualWidth:0.##} x {target.ActualHeight:0.##}，窗口内位置 {origin.X:0.##},{origin.Y:0.##}");
        Console.WriteLine();
        Console.WriteLine("祖先链（内层到外层）：");

        var problems = 0;
        for (var index = 0; index < chain.Count; index++)
        {
            var element = chain[index];
            var label = string.IsNullOrEmpty(element.Name) ? "" : $"#{element.Name}";
            var margin = element.Margin;
            Console.Write($"  {index,2} {element.GetType().Name,-20}{label,-24}" +
                          $"{element.ActualWidth,7:0.##} x {element.ActualHeight,6:0.##}  " +
                          $"Margin {margin.Left:0.##},{margin.Top:0.##},{margin.Right:0.##},{margin.Bottom:0.##}");

            if (index + 1 < chain.Count)
            {
                // chain is built from the inside out, so the parent of entry i is entry i+1.
                var parent = chain[index + 1];
                var padding = parent is System.Windows.Controls.Border border ? border.Padding : default;
                var availableWidth = parent.ActualWidth - padding.Left - padding.Right;
                var availableHeight = parent.ActualHeight - padding.Top - padding.Bottom;
                var neededWidth = element.ActualWidth + margin.Left + margin.Right;
                var neededHeight = element.ActualHeight + margin.Top + margin.Bottom;
                var overWidth = neededWidth - availableWidth;
                var overHeight = neededHeight - availableHeight;
                if (overWidth > 0.5)
                {
                    problems++;
                    Console.Write($"   <- 宽 {neededWidth:0.##} > 可用 {availableWidth:0.##}（超 {overWidth:0.##}）");
                }
                if (overHeight > 0.5)
                {
                    problems++;
                    Console.Write($"   <- 高 {neededHeight:0.##} > 可用 {availableHeight:0.##}（超 {overHeight:0.##}）");
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine(problems == 0
            ? "链上没有元素超出它拿到的宽度。"
            : $"{problems} 层超出了可用宽度，圆角就是在那里被裁掉的。");
        return problems == 0 ? 0 : 1;
    }

    /// <summary>
    /// Selects every tab above a named element, outermost included, so the capture
    /// shows the page that was changed rather than whichever one opens first.
    /// </summary>
    private static void SelectTabContaining(FrameworkElement window, string elementName)
    {
        if (window.FindName(elementName) is not DependencyObject node) return;
        while (node is not null)
        {
            if (node is TabItem tab) tab.IsSelected = true;
            // The logical tree reaches a tab's content even before that tab has been
            // selected and connected to the visual tree, which is exactly the case
            // this has to handle.
            node = LogicalTreeHelper.GetParent(node) ?? VisualTreeHelper.GetParent(node);
        }
    }
}
