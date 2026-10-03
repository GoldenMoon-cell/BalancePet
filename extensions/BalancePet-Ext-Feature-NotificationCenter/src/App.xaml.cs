using System.Diagnostics;
using System.Windows;

namespace BalancePet.NotificationCenter;

public partial class App : Application
{
    private const string PresenterMutexName = @"Local\BalancePet.NotificationPresenter.v1";
    private const string ShowPanelEventName = @"Local\BalancePet.NotificationCenter.ShowPanel.v1";
    private Mutex? _presentationMutex;
    private EventWaitHandle? _showPanelEvent;
    private CancellationTokenSource? _showPanelCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Sketches of where the hover information could go instead. Drawn, not built: they
        // answer "does this belong to the pet" before anything is rebuilt.
        if (ValueAfter(e.Args, "--sketch") is { Length: > 0 } sketchOutput)
        {
            Shutdown(RingSketches.Run(
                sketchOutput,
                ValueAfter(e.Args, "--style") ?? "a",
                ValueAfter(e.Args, "--pet") ?? "bottom-right",
                ValueAfter(e.Args, "--pet-image"),
                string.Equals(ValueAfter(e.Args, "--plate"), "dark", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        // A review render, and nothing else: no takeover marker, no windows, no listener.
        // Handled before any of that, so asking for a picture never disturbs a running copy
        // and never claims to be the notification presenter.
        if (ValueAfter(e.Args, "--preview") is { Length: > 0 } output)
        {
            var position = ValueAfter(e.Args, "--pet") ?? "bottom-right";
            Shutdown(PreviewRenderer.Run(
                output, position, PreviewItems.Load(ValueAfter(e.Args, "--items")), ValueAfter(e.Args, "--pet-image")));
            return;
        }

        var isWindows11 = Environment.OSVersion.Version.Build >= 22000;
        Resources["WindowCornerRadius"] = isWindows11 ? new CornerRadius(10) : new CornerRadius(4);
        Resources["ControlCornerRadius"] = isWindows11 ? new CornerRadius(7) : new CornerRadius(3);
        Resources["PanelCornerRadius"] = isWindows11 ? new CornerRadius(10) : new CornerRadius(4);
        Resources["CardCornerRadius"] = isWindows11 ? new CornerRadius(9) : new CornerRadius(3);
        if (!isWindows11)
        {
            Resources["WindowBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            Resources["TitleBarBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 232, 232));
            Resources["CardBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 247, 247));
            Resources["ControlHoverBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 225, 225));
            Resources["BorderBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 203, 203));
        }
        StopOlderInstances();
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        // Both of these create named objects, and both can be refused: an instance started
        // from a folder carrying a low integrity label cannot open a named event or mutex
        // that a medium-integrity instance created, and the failure arrives as
        // UnauthorizedAccessException out of OnStartup — before a window exists, so the
        // extension simply appears not to run. Measured, on this machine, against a plugin
        // installed by the program itself.
        //
        // Neither is essential. The listener only means the panel can be opened from the
        // host's menu; the mutex only means the host knows to keep its own bubbles quiet.
        // Failing at either is worth reporting and not worth refusing to start for.
        try
        {
            StartPanelListener(window);
            // Publish takeover only after the event store and bubble listener are ready.
            _presentationMutex = new Mutex(false, PresenterMutexName);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or WaitHandleCannotBeOpenedException
            or System.IO.IOException or NotSupportedException or System.Security.SecurityException)
        {
            LogUnavailable(error);
        }
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) window.Show();
    }

    /// <summary>
    /// Says why a named object was unavailable, next to the extension so it can be found.
    /// </summary>
    private static void LogUnavailable(Exception error)
    {
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BalancePet");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(directory, "notification-center.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 与主程序的命名对象不可用：{error.Message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Being unable to write down a failure must not become one.
        }
    }

    /// <summary>The argument after a named switch, or null when the switch is absent.</summary>
    private static string? ValueAfter(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showPanelCancellation?.Cancel();
        _showPanelEvent?.Set();
        _showPanelCancellation?.Dispose();
        _showPanelCancellation = null;
        _showPanelEvent?.Dispose();
        _showPanelEvent = null;
        _presentationMutex?.Dispose();
        _presentationMutex = null;
        base.OnExit(e);
    }

    private void StartPanelListener(MainWindow window)
    {
        _showPanelEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowPanelEventName);
        _showPanelCancellation = new CancellationTokenSource();
        var signal = _showPanelEvent;
        var cancellation = _showPanelCancellation.Token;
        _ = Task.Run(() =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (!signal.WaitOne(500)) continue;
                if (cancellation.IsCancellationRequested) return;
                Dispatcher.BeginInvoke(() =>
                {
                    window.Show();
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                });
            }
        }, cancellation);
    }

    private static void StopOlderInstances()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id) continue;
                try
                {
                    var path = process.MainModule?.FileName ?? "";
                    if (!path.Contains(@"BalancePet\extensions\balancepet.ext.feature.notification-center", StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1500);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
    }
}
