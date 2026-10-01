using System.IO;
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
/// while nothing appears in front of whatever the user is doing. Nothing is saved:
/// the settings file is read and the process exits.
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
        var window = new SettingsWindow(store, new DpapiTokenStore(), settings)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            Topmost = false
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            SelectTabContaining(window, "PetPreviewGrid");
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
    /// Selects whichever tab holds a named element, so the capture shows the page
    /// that was changed instead of whichever one happens to open first.
    /// </summary>
    private static void SelectTabContaining(FrameworkElement window, string elementName)
    {
        if (window.FindName(elementName) is not DependencyObject node) return;
        while (node is not null and not TabItem)
        {
            // The logical tree reaches a tab's content even before that tab has been
            // selected and connected to the visual tree, which is exactly the case
            // this has to handle.
            node = LogicalTreeHelper.GetParent(node) ?? VisualTreeHelper.GetParent(node);
        }
        if (node is TabItem tab) tab.IsSelected = true;
    }
}
