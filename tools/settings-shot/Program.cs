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

            var width = (int)Math.Ceiling(window.ActualWidth);
            var height = (int)Math.Ceiling(window.ActualHeight);
            if (width <= 0 || height <= 0)
            {
                Console.WriteLine("窗口没有布局尺寸，无法截图。");
                return 2;
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
