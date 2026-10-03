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

        InstallCrashHandlers();

        // Coordinating with another copy is best effort, because the named objects can be
        // refused outright. Measured: an instance started from a folder carrying a low
        // integrity label cannot open the event a medium-integrity instance created, and
        // creating it throws UnauthorizedAccessException out of OnStartup — before
        // anything has been drawn, so the program appears never to start. Failing to
        // coordinate is not a reason to refuse to run: the worst case is a second pet,
        // against no pet at all.
        try
        {
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
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.IO.IOException
            or WaitHandleCannotBeOpenedException or NotSupportedException or System.Security.SecurityException)
        {
            // Carries on as the only instance. Recorded rather than swallowed silently: if
            // this is why a second pet appeared, the reason should be findable.
            Services.CrashLog.Write("单实例协调不可用", error);
            _activationEvent = null;
            _instanceMutex = null;
            _ownsInstance = true;
        }

        if (!_ownsInstance)
        {
            _activationEvent?.Set();
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

    /// <summary>
    /// Stops one fault from ending the program, and writes down what it was.
    /// </summary>
    /// <remarks>
    /// A desktop pet has no console and no error window of its own, so an unhandled
    /// exception simply made it disappear, with nothing left behind to read. Measured in
    /// this program's own event log: a font family whose file is missing threw while the
    /// settings window was building its font list, and a catalog load used an HTTP client
    /// the window had already disposed. Neither concerns the pet or the polling, and both
    /// ended the process.
    ///
    /// The dispatcher's handler therefore keeps running, says so in the bubble, and records
    /// the exception. A fault inside a handler is not proof that the rest of the program is
    /// unsound, and a pet that vanishes without a word is worse than one that mentions a
    /// problem and carries on. The other two handlers record what the runtime will not let
    /// anything continue past.
    /// </remarks>
    internal void InstallCrashHandlers()    {
        DispatcherUnhandledException += (_, args) =>
        {
            Services.CrashLog.Write("界面线程", args.Exception);
            args.Handled = true;
            try { (MainWindow as MainWindow)?.MentionProblem(); }
            catch (Exception)
            {
                // Reporting a fault must not become another one: whatever went wrong may
                // have been in the drawing this would use to say so.
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception error) Services.CrashLog.Write("运行时", error);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.CrashLog.Write("后台任务", args.Exception);
            args.SetObserved();
        };
    }

    private void ListenForActivation()
    {        if (_activationEvent is null || _activationCancellation is null) return;

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
