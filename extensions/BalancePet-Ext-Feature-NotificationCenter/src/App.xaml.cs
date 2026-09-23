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
        StartPanelListener(window);
        // Publish takeover only after the event store and bubble listener are ready.
        _presentationMutex = new Mutex(false, PresenterMutexName);
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) window.Show();
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
