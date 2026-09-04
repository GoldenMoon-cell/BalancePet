namespace BalancePet.Wpf;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\BalancePet.Wpf.SingleInstance.v1";
    private const string ActivationEventName = @"Local\BalancePet.Wpf.Activate.v1";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationListener;
    private bool _ownsInstance;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        _ownsInstance = createdNew;

        if (!createdNew)
        {
            try
            {
                _ownsInstance = _instanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // A previous process crashed without releasing the mutex.
                _ownsInstance = true;
            }
        }

        if (!_ownsInstance)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        _activationCancellation = new CancellationTokenSource();
        _activationListener = Task.Run(ListenForActivation);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        try { _activationListener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }

        _activationEvent?.Dispose();
        _activationCancellation?.Dispose();
        if (_ownsInstance)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void ListenForActivation()
    {
        if (_activationEvent is null || _activationCancellation is null) return;

        try
        {
            var handles = new WaitHandle[] { _activationEvent, _activationCancellation.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                Dispatcher.BeginInvoke(new Action(ActivateMainWindow));
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposes the synchronization primitives after canceling.
        }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not System.Windows.Window window) return;
        if (!window.IsVisible) window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;

        // Briefly raising the window helps Windows bring a hidden pet back to
        // the foreground when a user launches the executable a second time.
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
    }
}
