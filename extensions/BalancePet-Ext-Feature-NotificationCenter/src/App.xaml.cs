using System.Diagnostics;
using System.Windows;

namespace BalancePet.NotificationCenter;

public partial class App : Application
{
    private const string ShowPanelEventName = @"Local\BalancePet.NotificationCenter.ShowPanel.v1";
    private EventWaitHandle? _showPanelEvent;
    private CancellationTokenSource? _showPanelCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Sketches of where the hover information could go instead. Drawn, not built: they
        // answer "does this belong to the pet" before anything is rebuilt.
        // The same idea as a sequence of frames, for the transition between the two
        // arrangements — which cannot be judged from a still.
        if (ValueAfter(e.Args, "--morph") is { Length: > 0 } morphDirectory)
        {
            var frameCount = int.TryParse(ValueAfter(e.Args, "--frames"), out var parsedFrames) ? parsedFrames : 14;
            Shutdown(RingSketches.RenderMorph(
                morphDirectory, frameCount,
                ValueAfter(e.Args, "--pet") ?? "corner",
                ValueAfter(e.Args, "--pet-image"),
                string.Equals(ValueAfter(e.Args, "--plate"), "dark", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        // A sketch of this window, redrawn in the plates' language, for review.
        // A movement captured as frames: the drag that gathers the values, or the entrance.
        if (ValueAfter(e.Args, "--frames") is { Length: > 0 } framesDirectory)
        {
            var frameMode = e.Args.Contains("--orbit", StringComparer.OrdinalIgnoreCase) ? "orbit"
                : e.Args.Contains("--enter", StringComparer.OrdinalIgnoreCase) ? "enter" : "track";
            var count = int.TryParse(ValueAfter(e.Args, "--count"), out var wanted) ? Math.Clamp(wanted, 2, 60) : 14;
            var petArt = ValueAfter(e.Args, "--pet-image");
            Shutdown(PreviewRenderer.RenderFrames(framesDirectory, frameMode, PreviewItems.Current(), petArt, count));
            return;
        }

        // The gathering rule, checked against real geometry rather than watched in an
        // animation: a transition can look right while gathering when it did not need to.
        if (e.Args.Contains("--ring-rules", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(RingLayoutCheck.Run());
            return;
        }

        // The real window, rendered off-screen for review. Not a sketch: this is the actual
        // window object, so what it shows is what it will show.
        if (ValueAfter(e.Args, "--shot") is { Length: > 0 } shotPath)
        {
            var shotWindow = new MainWindow();
            if (ValueAfter(e.Args, "--section") is { Length: > 0 } shotSection) shotWindow.ShowSection(shotSection);
            // Shown outside the visible desktop so it is laid out and painted without
            // appearing over whatever the user is doing.
            shotWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            shotWindow.Left = -32000;
            shotWindow.Top = -32000;
            shotWindow.Show();
            shotWindow.UpdateLayout();
            // Let the rows finish arriving before the picture is taken: their entrance is
            // staggered, and a capture at layout time shows a window of invisible rows.
            var settle = new System.Windows.Threading.DispatcherFrame();
            var settleTimer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(900), System.Windows.Threading.DispatcherPriority.Background,
                (_, _) => settle.Continue = false, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            settleTimer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(settle);
            settleTimer.Stop();
            var shotWidth = (int)Math.Ceiling(shotWindow.ActualWidth);
            var shotHeight = (int)Math.Ceiling(shotWindow.ActualHeight);
            var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                shotWidth, shotHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            shot.Render(shotWindow);
            var shotEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            shotEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
            using (var shotStream = System.IO.File.Create(shotPath)) shotEncoder.Save(shotStream);
            Console.WriteLine($"窗口实拍已写出 {shotPath}  {shotWidth}×{shotHeight}");
            Shutdown(0);
            return;
        }

        // Three ways the hover plates could arrive, as frames: the difference is entirely in
        // the timing, so stills cannot answer it.
        if (ValueAfter(e.Args, "--enter") is { Length: > 0 } entranceDirectory)
        {
            var entranceFrames = int.TryParse(ValueAfter(e.Args, "--frames"), out var parsedEntranceFrames) ? parsedEntranceFrames : 20;
            Shutdown(EntranceSketches.Render(
                entranceDirectory,
                ValueAfter(e.Args, "--style") ?? "breathe",
                entranceFrames,
                ValueAfter(e.Args, "--pet") ?? "center",
                ValueAfter(e.Args, "--pet-image"),
                string.Equals(ValueAfter(e.Args, "--plate"), "dark", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        // The entrance as frames: the rows arrive one after another from the left, and that
        // is a question about timing rather than about layout.
        if (ValueAfter(e.Args, "--panel-anim") is { Length: > 0 } panelAnimation)
        {
            var panelFrames = int.TryParse(ValueAfter(e.Args, "--frames"), out var parsedPanelFrames) ? parsedPanelFrames : 20;
            var panelDark = string.Equals(ValueAfter(e.Args, "--theme"), "dark", StringComparison.OrdinalIgnoreCase);
            var panelSection = ValueAfter(e.Args, "--section") ?? "all";
            var panelPet = ValueAfter(e.Args, "--pet-image");
            System.IO.Directory.CreateDirectory(panelAnimation);
            for (var frame = 0; frame < panelFrames; frame++)
            {
                WindowSketch.Run(
                    System.IO.Path.Combine(panelAnimation, $"frame-{frame:00}.png"),
                    panelDark, panelPet, panelSection,
                    panelFrames == 1 ? 1 : frame / (double)(panelFrames - 1));
            }
            Shutdown(0);
            return;
        }

        if (ValueAfter(e.Args, "--panel") is { Length: > 0 } panelOutput)
        {
            Shutdown(WindowSketch.Run(
                panelOutput,
                string.Equals(ValueAfter(e.Args, "--theme"), "dark", StringComparison.OrdinalIgnoreCase),
                ValueAfter(e.Args, "--pet-image"),
                ValueAfter(e.Args, "--section") ?? "notice"));
            return;
        }

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
        PresenterMarker.Apply(ReadTakeover());
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
            // Publish takeover only after the event store and bubble listener are ready, and
            // honour the switch: the marker is what the host looks for, so holding it is the
            // whole of the mechanism. A refusal is reported by PresenterMarker and is not a
            // reason to stop.
            // The marker follows the remembered preference, applied above before the window
            // was built; nothing is forced here.
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

    /// <summary>The remembered choice, so the marker is held before the window opens.</summary>
    private static bool ReadTakeover()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BalancePet", "notification-center.json");
            if (!System.IO.File.Exists(path)) return true;
            using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            return !document.RootElement.TryGetProperty("takeover", out var value) || value.GetBoolean();
        }
        catch (Exception) { return true; }
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
        PresenterMarker.Release();
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
