using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Forms = System.Windows.Forms;
using BalancePet.Wpf.Models;
using BalancePet.Wpf.Services;

namespace BalancePet.Wpf;

public partial class MainWindow : Window
{
    private static readonly TimeSpan ManualRefreshCooldown = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshOperationTimeout = TimeSpan.FromSeconds(45);
    private const int EdgeSnapDistance = 32;
    private const double BubbleTextMaxWidth = 252;
    // A stop can arrive just before the corresponding start when a client
    // reconnects. Keep the short-lived marker only for that transport race.
    private static readonly TimeSpan TaskStopReorderWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetiredTaskWindow = TimeSpan.FromMinutes(2);

    private readonly SettingsStore _settingsStore = new();
    private readonly DpapiTokenStore _tokenStore = new();
    private readonly UsageLedgerStore _usageStore = new();
    private readonly HttpClient _httpClient = CreateBalanceHttpClient();
    private readonly HttpClient _updateHttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _floatTimer;
    private readonly CodexTaskBridge _codexTaskBridge = new();
    private readonly AccountStatusBridge _accountStatusBridge = new();
    private readonly DispatcherTimer _stateTimer;
    private readonly DispatcherTimer _inactiveTimer;
    private readonly DispatcherTimer _bubbleAnimationTimer;
    private readonly DispatcherTimer _bubbleContentTimer;
    private readonly DispatcherTimer _amountAnimationTimer;
    private readonly DispatcherTimer _codexHideTimer;
    private readonly DispatcherTimer _trayRecoveryTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly Dictionary<string, MonitorRuntime> _monitorStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Media.MediaPlayer _pressSound = new();
    private readonly System.Windows.Media.MediaPlayer _releaseSound = new();
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _trayDeepSeekStyleItem;
    private Forms.ToolStripMenuItem? _trayChatGptStyleItem;
    private Forms.ToolStripMenuItem? _trayMiniMaxStyleItem;
    private Forms.ToolStripMenuItem? _trayGeminiStyleItem;
    private Forms.ToolStripMenuItem? _trayGrokStyleItem;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _trayStyleItems = new(StringComparer.OrdinalIgnoreCase);
    private Forms.ToolStripMenuItem? _trayMonitorMenu;
    private Forms.ToolStripMenuItem? _trayShowItem;
    private Forms.ToolStripMenuItem? _trayRefreshItem;
    private Forms.ToolStripMenuItem? _traySettingsItem;
    private Forms.ToolStripMenuItem? _trayUpdateItem;
    private Forms.ToolStripMenuItem? _trayUsageItem;
    private Forms.ToolStripMenuItem? _trayExitItem;
    private Forms.ToolStripMenuItem? _trayStyleMenuItem;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _trayMonitorItems = new(StringComparer.OrdinalIgnoreCase);
    private System.Drawing.Icon? _trayImage;
    private HwndSource? _windowSource;
    private PetSettings _settings = new();
    private bool _closing;
    private System.Windows.Point _dragStart;
    private int _windowStartX;
    private int _windowStartY;
    private bool _dragging;
    private bool _dragMoved;
    private bool _codexShownPet;
    private bool _temporarilyShownForUpdate;
    private readonly Dictionary<string, double?> _codexStartBalances = new(StringComparer.OrdinalIgnoreCase);
    private double? _lastBalance;
    private double _todayUsage;
    private bool _hasBalance;
    private bool _refreshing;
    private CancellationTokenSource? _refreshCancellation;
    private DateTimeOffset _lastManualRefreshAttempt = DateTimeOffset.MinValue;
    private bool _updateBusy;
    private string? _configuredUpdateCheckMode;
    private int _trayRecoveryAttempts;
    private uint _taskbarCreatedMessage;
    private readonly HashSet<string> _activeCodexTurns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeTaskSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeTaskProfiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _recentTaskStops = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _retiredTaskKeys = new(StringComparer.Ordinal);
    private DateTimeOffset _lastAccountStatusAt = DateTimeOffset.MinValue;
    private string _lastAccountStatusKey = "";
    private double _bubbleAnimationProgress;
    private double _bubbleAnimationFrom;
    private double _bubbleAnimationTo;
    private bool _bubbleOpening;
    private string _pendingBubbleLabel = "";
    private string _pendingBubbleAmount = "";
    private string _pendingBubbleHint = "";
    private double _displayBalance;
    private double _amountFrom;
    private double _amountTo;
    private double _amountAnimationProgress;
    private string _amountCurrency = "USD";
    private bool _hasDisplayAmount;
    private string? _activePetImagePath;
    private bool _lockedPressed;
    private bool _mousePressed;
    private string _lockedKind = "body";
    private System.Windows.Point _lockedStart;
    private double _interactionX;
    private double _interactionY;
    private double _interactionTargetX;
    private double _interactionTargetY;
    private double _squashProgress;
    private double _squashFrom;
    private double _squashTo;
    private double _squashClock;
    private bool _squashAnimating;
    private long _lastAnimationTick;
    private int _interactionStreak;
    private DateTimeOffset _lastInteractionAt = DateTimeOffset.MinValue;
    private PetVisualState _visualState = PetVisualState.Idle;
    private TranslateTransform _petTranslate = new();
    private ScaleTransform _petScale = new(1, 1);
    private RotateTransform _petRotate = new();
    private IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    private static HttpClient CreateBalanceHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // Relay services and NAT devices may drop long-idle connections. Rotate
            // pooled connections periodically so a later manual refresh gets a
            // fresh route without requiring an application restart.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const int CodexDoneDurationMs = 6200;

    private enum PetVisualState
    {
        Idle,
        Loading,
        Success,
        Low,
        Error,
        Clicked,
        CodexWorking,
        CodexDone,
        Inactive
    }

    private sealed class MonitorRuntime
    {
        public MonitorProfile Profile { get; }
        public UsageLedgerStore UsageStore { get; }
        public BalanceCacheStore CacheStore { get; }
        public DateTimeOffset LastRefreshAttempt { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset LastErrorNotification { get; set; } = DateTimeOffset.MinValue;
        public double? LastBalance { get; set; }
        public double TodayUsage { get; set; }
        public bool HasBalance { get; set; }
        public bool Refreshing { get; set; }
        public string? LastError { get; set; }
        public string TokenFingerprint { get; set; } = "";

        public MonitorRuntime(MonitorProfile profile)
        {
            Profile = profile;
            UsageStore = new UsageLedgerStore(profile.Id);
            CacheStore = new BalanceCacheStore(profile.Id);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

    public MainWindow()
    {
        InitializeComponent();
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        // A one-second scheduler lets profiles keep their own refresh interval while
        // still enforcing the 30-second minimum for every endpoint.
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(false);
        _updateService = new UpdateService(_updateHttpClient);
        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _bubbleTimer.Tick += (_, _) => HideBubble();
        _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _floatTimer.Tick += (_, _) => AnimatePet();
        _codexTaskBridge.ActivityReceived += OnCodexTaskActivityReceived;
        _accountStatusBridge.ActivityReceived += OnAccountStatusReceived;
        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _stateTimer.Tick += (_, _) => RestoreSteadyVisualState();
        _inactiveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _inactiveTimer.Tick += (_, _) => OnInactiveTimerElapsed();
        _bubbleAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _bubbleAnimationTimer.Tick += (_, _) => AnimateBubble();
        _bubbleContentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _bubbleContentTimer.Tick += (_, _) => AnimateBubbleContent();
        _amountAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _amountAnimationTimer.Tick += (_, _) => AnimateAmount();
        _codexHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _codexHideTimer.Tick += (_, _) => HideAfterCodexCompletion();
        _trayRecoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trayRecoveryTimer.Tick += (_, _) => RecoverTrayRegistration();
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _updateTimer.Tick += async (_, _) => await CheckForAutomaticUpdatesAsync();
        SourceInitialized += (_, _) =>
        {
            HideFromTaskSwitcher();
            RegisterTaskbarCreatedHook();
        };
        Loaded += async (_, _) =>
        {
            LoadSettingsAndPosition();
            if (ShowPostUpdateConfirmation()) return;
            await RefreshAsync(false);
        };
        Closing += (_, e) =>
        {
            if (!_closing) { e.Cancel = true; Hide(); return; }
            _refreshCancellation?.Cancel();
            _trayRecoveryTimer.Stop();
            _updateTimer.Stop();
            _codexTaskBridge.Dispose();
            _accountStatusBridge.Dispose();
            UnregisterTaskbarCreatedHook();
            SavePosition(); DisposeTray(); _httpClient.Dispose(); _updateHttpClient.Dispose();
        };
    }

    private void LoadSettingsAndPosition()
    {
        _settings = _settingsStore.Load();
        RebuildMonitorStates();
        // Release folders are versioned, so refresh the Run entry to this
        // executable whenever startup is enabled or an older entry remains.
        var startupRegistered = StartupManager.IsEnabled();
        if (startupRegistered || _settings.StartWithWindows)
        {
            if (StartupManager.SetEnabled(true) && !_settings.StartWithWindows)
            {
                _settings.StartWithWindows = true;
                _settingsStore.Save(_settings);
            }
        }
        var scale = Math.Clamp(_settings.Scale, 0.6, 1.4);
        PetSurface.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        LoadPetVisual(_visualState);
        ApplyFlipVisuals();
        EnsurePetTransforms();
        var workArea = SystemParameters.WorkArea;
        var dpi = GetDpiForWindow(WindowHandle);
        var physicalToDip = dpi is > 0 and not 96 ? 96.0 / dpi : 1.0;
        if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
        {
            var savedLeft = _settings.WindowX * physicalToDip;
            var savedTop = _settings.WindowY * physicalToDip;
            Left = Math.Clamp(savedLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Clamp(savedTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }
        else { Left = workArea.Right - Width - 24; Top = workArea.Bottom - Height - 24; }
        ConfigureRefreshTimer();
        ConfigureUpdateChecks();
        _floatTimer.Start();
        ResetInactiveTimer();
        ConfigureSounds();
        _codexTaskBridge.Stop();
        _accountStatusBridge.Stop();
        _activeCodexTurns.Clear();
        _activeTaskSources.Clear();
        _activeTaskProfiles.Clear();
        _recentTaskStops.Clear();
        _retiredTaskKeys.Clear();
        _codexStartBalances.Clear();
        _codexShownPet = false;
        if (_settings.CodexTaskIntegration) _codexTaskBridge.Start();
        if (_settings.AccountStatusIntegration) _accountStatusBridge.Start();
        SetupTray();
        UpdateTrayMonitorMenu();
        UpdateContextMonitorMenu();
        ApplyLocalization();
        UpdatePetStyleMenuChecks();
        // Enabling Codex task following must not change the pet's visibility.
        // The pet stays visible after startup and settings reload; hiding is
        // still available from the tray or the window close control.
        if (!IsVisible)
        {
            Show();
        }
    }

    private void RebuildMonitorStates()
    {
        _monitorStates.Clear();
        _settings.Monitors ??= new List<MonitorProfile>();
        if (_settings.Monitors.Count == 0)
        {
            _settings.Monitors.Add(new MonitorProfile
            {
                Id = "default",
                Name = "默认账户",
                PresetId = BalancePresetCatalog.Custom,
                Endpoint = _settings.Endpoint,
                AuthMode = _settings.AuthMode,
                HeaderName = _settings.HeaderName,
                TokenBlob = _settings.TokenBlob,
                BalancePath = _settings.BalancePath,
                Currency = _settings.Currency,
                RefreshSeconds = Math.Max(30, _settings.RefreshSeconds),
                AutoRefreshEnabled = _settings.AutoRefreshEnabled,
                LowThreshold = _settings.LowThreshold
            });
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _settings.Monitors)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !seen.Add(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "监控账户" : profile.Name.Trim();
            profile.RefreshSeconds = Math.Max(30, profile.RefreshSeconds);
            profile.Currency = string.IsNullOrWhiteSpace(profile.Currency) ? "USD" : profile.Currency.Trim().ToUpperInvariant();
            profile.PresetId = BalancePresetCatalog.NormalizeId(profile.PresetId);
            if (BalancePresetCatalog.UsesSiteUrl(profile.PresetId))
                BalancePresetCatalog.Apply(profile, profile.PresetId, BalancePresetCatalog.ResolveSiteUrl(profile));
            var runtime = new MonitorRuntime(profile);
            try { runtime.TokenFingerprint = FingerprintToken(_tokenStore.Unprotect(profile.TokenBlob)); }
            catch (Exception error) when (error is FormatException or System.Security.SecurityException or CryptographicException) { }
            if (runtime.CacheStore.TryLoad(out var cached))
            {
                runtime.LastBalance = cached.Amount;
                runtime.HasBalance = true;
            }
            _monitorStates[profile.Id] = runtime;
        }
        if (!_monitorStates.ContainsKey(_settings.SelectedMonitorId)) _settings.SelectedMonitorId = _settings.Monitors[0].Id;
        SyncSelectedMonitorState();
    }

    private MonitorRuntime? SelectedMonitor => _monitorStates.TryGetValue(_settings.SelectedMonitorId, out var runtime)
        ? runtime
        : _monitorStates.Values.FirstOrDefault();

    private bool IsSelectedMonitor(MonitorRuntime runtime) => string.Equals(runtime.Profile.Id, _settings.SelectedMonitorId, StringComparison.OrdinalIgnoreCase);

    private void ConfigureRefreshTimer()
    {
        var shouldPoll = _monitorStates.Values.Any(runtime => runtime.Profile.Enabled
            && runtime.Profile.AutoRefreshEnabled
            && !string.IsNullOrWhiteSpace(runtime.Profile.Endpoint));
        if (!shouldPoll)
        {
            _refreshTimer.Stop();
            return;
        }

        _refreshTimer.Interval = TimeSpan.FromSeconds(1);
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    private void SyncSelectedMonitorState()
    {
        var selected = SelectedMonitor;
        if (selected is null) { _lastBalance = null; _hasBalance = false; _todayUsage = 0; return; }
        _settings.SelectedMonitorId = selected.Profile.Id;
        _lastBalance = selected.LastBalance;
        _hasBalance = selected.HasBalance;
        _todayUsage = selected.TodayUsage;
        _amountCurrency = string.IsNullOrWhiteSpace(selected.Profile.Currency) ? "USD" : selected.Profile.Currency;
    }

    private async Task RefreshAsync(bool manual, bool selectedOnly = false, bool force = false)
    {
        if (_closing) return;
        if (manual) ResetInactiveTimer();
        var selected = SelectedMonitor;
        if (selectedOnly)
        {
            if (_monitorStates.Count > 0 && !_monitorStates.Values.Any(runtime => runtime.Profile.Enabled))
            {
                SetUnavailable("没有启用账户", "--", "请在设置中至少启用一个监控账户", manual);
                return;
            }

            if (selected is null)
            {
                SetUnavailable("没有监控账户", "--", "请先添加监控账户", manual);
                return;
            }

            if (!selected.Profile.Enabled)
            {
                SetUnavailable("账户未启用", "--", $"{selected.Profile.Name} 未启用监控，请先在设置中启用", manual);
                return;
            }

            if (string.IsNullOrWhiteSpace(selected.Profile.Endpoint))
            {
                SetUnavailable("接口未配置", "--", $"请先填写 {selected.Profile.Name} 的余额 API 地址", manual);
                return;
            }
        }

        var profiles = _monitorStates.Values
            .Where(runtime => runtime.Profile.Enabled && !string.IsNullOrWhiteSpace(runtime.Profile.Endpoint)
                && (selectedOnly || runtime.Profile.AutoRefreshEnabled || force)
                && (!selectedOnly || IsSelectedMonitor(runtime)))
            .ToArray();
        if (profiles.Length == 0)
        {
            if (!selectedOnly && !force && !_monitorStates.Values.Any(runtime => runtime.Profile.Enabled && runtime.Profile.AutoRefreshEnabled)) return;
            var hasAccounts = _monitorStates.Count > 0;
            var hasEnabled = _monitorStates.Values.Any(runtime => runtime.Profile.Enabled);
            if (!hasAccounts) SetUnavailable("没有监控账户", "--", "请先添加监控账户", manual);
            else if (!hasEnabled) SetUnavailable("没有启用账户", "--", "请在设置中至少启用一个监控账户", manual);
            else SetUnavailable("接口未配置", "--", "请在设置中填写已启用账户的余额 API 地址", manual);
            return;
        }
        if (_refreshing)
        {
            if (manual) ShowBubble("正在刷新", "--", "上一轮查询尚未完成");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var due = profiles.Where(runtime =>
            force || (manual
                ? now - _lastManualRefreshAttempt >= ManualRefreshCooldown
                : now - runtime.LastRefreshAttempt >= TimeSpan.FromSeconds(Math.Max(30, runtime.Profile.RefreshSeconds))))
            .ToArray();
        if (due.Length == 0)
        {
            if (manual)
            {
                var remaining = Math.Max(1, Math.Ceiling((ManualRefreshCooldown - (now - _lastManualRefreshAttempt)).TotalSeconds));
                ShowBubble("请稍候", $"{remaining:0} 秒", "手动刷新至少间隔 5 秒；自动刷新间隔可在设置中调整");
            }
            return;
        }
        _refreshing = true;
        using var refreshCancellation = new CancellationTokenSource(RefreshOperationTimeout);
        _refreshCancellation = refreshCancellation;
        var selectedRefreshStarted = false;
        try
        {
            selectedRefreshStarted = due.Any(IsSelectedMonitor);
            if (selectedRefreshStarted)
            {
                SetStatus("正在查询");
                if (_activeCodexTurns.Count == 0) SetVisualState(PetVisualState.Loading);
                if (manual) ShowBubble("正在刷新", "--", due.Length == 1 ? $"正在联系 {due[0].Profile.Name}" : $"正在查询 {due.Length} 个账户");
            }
            await Task.WhenAll(due.Select(runtime => RefreshMonitorAsync(runtime, manual, refreshCancellation.Token)));
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
        {
            // A settings reload or the operation deadline canceled this round.
            // Do not replace a valid cached balance with a misleading error state.
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, refreshCancellation)) _refreshCancellation = null;
            _refreshing = false;
            ReconcileSelectedRefreshUi(manual, selectedRefreshStarted);
        }
    }

    private void ReconcileSelectedRefreshUi(bool manual, bool selectedRefreshStarted)
    {
        if (_closing || !selectedRefreshStarted) return;

        // A task event can race the HTTP completion. Never leave the loading
        // artwork or its pending bubble behind after the request has ended.
        if (_activeCodexTurns.Count > 0)
        {
            if (_visualState == PetVisualState.Loading)
            {
                SetVisualState(PetVisualState.CodexWorking);
                ShowBubble(
                    $"{CurrentTaskSourceLabel()} 工作中",
                    _activeCodexTurns.Count == 1 ? "正在处理" : $"{_activeCodexTurns.Count} 个任务",
                    "任务完成或停止后会自动切换状态");
            }
            return;
        }

        if (_visualState != PetVisualState.Loading) return;
        var selected = SelectedMonitor;
        if (selected?.HasBalance == true && selected.LastBalance.HasValue && selected.LastError is null)
        {
            var low = selected.LastBalance.Value <= selected.Profile.LowThreshold;
            SetVisualState(low ? PetVisualState.Low : PetVisualState.Success, low ? 0 : 1800);
            if (manual)
            {
                var currency = string.IsNullOrWhiteSpace(selected.Profile.Currency) ? "USD" : selected.Profile.Currency;
                ShowBubble("账户余额", $"{selected.LastBalance.Value:0.00} {currency}", $"{selected.Profile.Name} · 刷新完成");
            }
        }
        else
        {
            RestoreSteadyVisualState();
        }
    }

    private void SetUnavailable(string title, string amount, string detail, bool notify)
    {
        SetStatus(title);
        if (!notify) return;
        if (_activeCodexTurns.Count == 0) RestoreSteadyVisualState();
        ShowBubble(title, amount, detail);
    }

    private async Task RefreshMonitorAsync(MonitorRuntime runtime, bool manual, CancellationToken cancellationToken)
    {
        var attemptAt = DateTimeOffset.UtcNow;
        runtime.LastRefreshAttempt = attemptAt;
        if (manual) _lastManualRefreshAttempt = attemptAt;
        runtime.Refreshing = true;
        try
        {
            var token = _tokenStore.Unprotect(runtime.Profile.TokenBlob);
            var snapshot = await new JsonBalanceProvider(_httpClient).FetchWithRetryAsync(runtime.Profile, token, cancellationToken);
            if (!string.IsNullOrWhiteSpace(snapshot.Currency)) runtime.Profile.Currency = snapshot.Currency;
            var hadBalance = runtime.HasBalance;
            var wasLow = hadBalance && runtime.LastBalance <= runtime.Profile.LowThreshold;
            var observation = runtime.UsageStore.Record(snapshot.Amount, snapshot.Currency, snapshot.UpdatedAt);
            runtime.CacheStore.Save(snapshot);
            runtime.LastBalance = snapshot.Amount;
            runtime.HasBalance = true;
            runtime.TodayUsage = observation.TodayUsage;
            runtime.LastError = null;

            if (!IsSelectedMonitor(runtime)) return;
            SyncSelectedMonitorState();
            SetStatus(snapshot.Amount <= runtime.Profile.LowThreshold ? "余额偏低" : "查询成功");
            if (manual) PlaySound(_releaseSound);
            // Automatic polling keeps the task artwork authoritative. A user
            // initiated refresh must still show its result even if an older
            // client missed its Stop event; the state timer returns to working
            // afterward when a task is genuinely still active.
            if (_activeCodexTurns.Count > 0 && _visualState == PetVisualState.CodexWorking && !manual) return;
            if (observation.Spent > 0.000001)
            {
                SetVisualState(PetVisualState.Clicked, 1200);
                ShowBubble("本次消耗", $"-{observation.Spent:0.00} {observation.Currency}", $"{runtime.Profile.Name} · 当前 {snapshot.Amount:0.00} {observation.Currency} · 今日已用 {observation.TodayUsage:0.00}");
            }
            else
            {
                var low = snapshot.Amount <= runtime.Profile.LowThreshold;
                var temporaryMs = low && _activeCodexTurns.Count > 0 && manual ? 1800 : low ? 0 : 1800;
                SetVisualState(low ? PetVisualState.Low : PetVisualState.Success, temporaryMs);
                if (low && (!wasLow || !hadBalance))
                    ShowSystemNotification($"{runtime.Profile.Name} 余额偏低", $"当前余额 {snapshot.Amount:0.00} {snapshot.Currency}", Forms.ToolTipIcon.Warning);
                if (manual || !hadBalance) ShowBubble("账户余额", $"{snapshot.Amount:0.00} {snapshot.Currency}", $"{runtime.Profile.Name} · 更新于 {snapshot.UpdatedAt:HH:mm:ss} · 今日已用 {observation.TodayUsage:0.00}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is expected when settings are reloaded or a request
            // exceeds the bounded refresh window; leave the previous cache intact.
            if (IsSelectedMonitor(runtime))
            {
                SyncSelectedMonitorState();
                SetStatus("刷新失败");
                if (_activeCodexTurns.Count == 0)
                {
                    RestoreSteadyVisualState();
                    if (manual) ShowBubble("刷新失败", "--", $"{runtime.Profile.Name} 请求超时或已取消，请稍后重试");
                }
                else if (_visualState != PetVisualState.CodexWorking)
                {
                    SetVisualState(PetVisualState.CodexWorking);
                    ShowBubble(
                        $"{CurrentTaskSourceLabel()} 工作中",
                        _activeCodexTurns.Count == 1 ? "正在处理" : $"{_activeCodexTurns.Count} 个任务",
                        "任务完成或停止后会自动切换状态");
                }
            }
        }
        catch (Exception error)
        {
            runtime.LastError = error.Message;
            if (!IsSelectedMonitor(runtime)) return;
            SyncSelectedMonitorState();
            SetStatus("刷新失败");
            if (_activeCodexTurns.Count > 0)
            {
                if (_visualState != PetVisualState.CodexWorking)
                {
                    SetVisualState(PetVisualState.CodexWorking);
                    ShowBubble(
                        $"{CurrentTaskSourceLabel()} 工作中",
                        _activeCodexTurns.Count == 1 ? "正在处理" : $"{_activeCodexTurns.Count} 个任务",
                        "任务完成或停止后会自动切换状态");
                }
                return;
            }
            SetVisualState(PetVisualState.Error);
            var detail = error.Message.Length > 90 ? error.Message[..90] + "…" : error.Message;
            if (runtime.CacheStore.TryLoad(out var cached))
            {
                runtime.LastBalance = cached.Amount;
                runtime.HasBalance = true;
                SyncSelectedMonitorState();
                AnimateAmountTo(cached.Amount, cached.Currency);
                ShowBubble("上次余额", $"{cached.Amount:0.00} {cached.Currency}", $"{runtime.Profile.Name} 网络波动，暂用缓存 · {detail}");
            }
            else ShowBubble("刷新失败", "--", $"{runtime.Profile.Name} · {detail}");
            if (DateTimeOffset.Now - runtime.LastErrorNotification > TimeSpan.FromMinutes(10))
            {
                ShowSystemNotification($"{runtime.Profile.Name} 刷新失败", detail, Forms.ToolTipIcon.Error);
                runtime.LastErrorNotification = DateTimeOffset.Now;
            }
        }
        finally { runtime.Refreshing = false; }
    }

    private void SetStatus(string status)
    {
        StatusDot.Fill = status switch
        {
            "正在查询" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 169, 61)),
            "查询成功" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(53, 183, 125)),
            "刷新失败" or "余额偏低" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 91, 113)),
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(145, 162, 200))
        };
    }

    private void SetVisualState(PetVisualState state, int temporaryMs = 0)
    {
        if (_mousePressed && _settings.InteractionEffects && (state != PetVisualState.Clicked || temporaryMs > 0)) return;
        _visualState = state;
        LoadPetVisual(state);
        _stateTimer.Stop();
        if (temporaryMs > 0)
        {
            _stateTimer.Interval = TimeSpan.FromMilliseconds(temporaryMs);
            _stateTimer.Start();
        }
        EnsurePetTransforms();
    }

    private void LoadPetVisual(PetVisualState state)
    {
        var style = NormalizePetStyle(_settings.PetStyle);
        var stateName = state switch
        {
            PetVisualState.CodexWorking => "codex-working",
            PetVisualState.CodexDone => "codex-done",
            _ => state.ToString().ToLowerInvariant()
        };
        var styleDirectory = PetStyleCatalog.ResolveAssetDirectory(style);
        var baseName = style == "chatgpt" ? "chatgpt-dragon.png" : "pet.png";
        var statePath = System.IO.Path.Combine(styleDirectory, $"{stateName}.png");
        var styleIdlePath = System.IO.Path.Combine(styleDirectory, "idle.png");
        var basePath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", baseName);
        var selectedPath = File.Exists(statePath) ? statePath : File.Exists(styleIdlePath) ? styleIdlePath : basePath;
        if (!File.Exists(selectedPath) || string.Equals(_activePetImagePath, selectedPath, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(selectedPath, UriKind.Absolute);
            source.EndInit();
            source.Freeze();
            PetImage.Source = source;
            _activePetImagePath = selectedPath;
        }
        catch (IOException) { }
        catch (ArgumentException) { }
    }

    private void RestoreSteadyVisualState()
    {
        _stateTimer.Stop();
        if (_activeCodexTurns.Count > 0)
        {
            SetVisualState(PetVisualState.CodexWorking);
            return;
        }
        if (!_hasBalance) SetVisualState(PetVisualState.Idle);
        else if (_lastBalance <= (SelectedMonitor?.Profile.LowThreshold ?? _settings.LowThreshold)) SetVisualState(PetVisualState.Low);
        else SetVisualState(PetVisualState.Idle);
    }

    private void ResetInactiveTimer()
    {
        _inactiveTimer.Stop();
        _inactiveTimer.Start();
        if (_visualState == PetVisualState.Inactive) RestoreSteadyVisualState();
    }

    private void OnInactiveTimerElapsed()
    {
        _inactiveTimer.Stop();
        SetVisualState(PetVisualState.Inactive);
        if (!_settings.RandomEasterEggs || Random.Shared.Next(100) >= 35) return;

        var line = NormalizePetStyle(_settings.PetStyle) switch
        {
            "chatgpt" => ("霁珑在等你", "慢慢来", "需要时再喊我就好"),
            "minimax" => ("绯音在这里", "小憩一下", "回来后我还会继续守着余额"),
            "gemini" => ("星璃在这里", "小憩一下", "回来后我还会继续守着余额"),
            "grok" => ("烬斧在这里", "小憩一下", "回来后我还会继续守着余额"),
            _ => ("澜汐在这里", "小憩一下", "回来后我还会继续守着余额")
        };
        ShowBubble(line.Item1, line.Item2, line.Item3, TimeSpan.FromSeconds(4.2));
    }

    private void EnsurePetTransforms()
    {
        if (PetImage.RenderTransform is TransformGroup group && group.Children.Count >= 3)
        {
            _petTranslate = (TranslateTransform)group.Children[0];
            _petScale = (ScaleTransform)group.Children[1];
            _petRotate = (RotateTransform)group.Children[2];
            return;
        }

        _petTranslate = new TranslateTransform();
        _petScale = new ScaleTransform(1, 1);
        _petRotate = new RotateTransform();
        PetImage.RenderTransform = new TransformGroup { Children = { _petTranslate, _petScale, _petRotate } };
    }

    private void ApplyFlipVisuals()
    {
        StatusDot.HorizontalAlignment = _settings.Flipped ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
        StatusDot.Margin = _settings.Flipped ? new Thickness(22, 0, 0, 27) : new Thickness(0, 0, 22, 27);
        if (_settings.Flipped)
        {
            PetSurface.ClearValue(System.Windows.Controls.Canvas.RightProperty);
            System.Windows.Controls.Canvas.SetLeft(PetSurface, 0);
        }
        else
        {
            PetSurface.ClearValue(System.Windows.Controls.Canvas.LeftProperty);
            System.Windows.Controls.Canvas.SetRight(PetSurface, 0);
        }
        EnsurePetTransforms();
        _petScale.ScaleX = Math.Abs(_petScale.ScaleX) * (_settings.Flipped ? -1 : 1);
    }

    private void SavePosition()
    {
        if (!IsLoaded) return;
        if (GetWindowRect(WindowHandle, out var rect))
        {
            _settings.WindowX = rect.Left;
            _settings.WindowY = rect.Top;
        }
        else
        {
            _settings.WindowX = (int)Math.Round(Left);
            _settings.WindowY = (int)Math.Round(Top);
        }
        try { _settingsStore.Save(_settings); } catch (IOException) { }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        _mousePressed = true;
        ResetInactiveTimer();
        if (_settings.InteractionEffects)
        {
            // A press is a held state: do not let its state timer expire before release.
            SetVisualState(PetVisualState.Clicked);
            StartSquashAnimation(1);
        }
        if (_settings.InteractionMode == "locked")
        {
            _lockedPressed = true; _lockedStart = PointToScreen(e.GetPosition(this));
            var local = e.GetPosition(PetSurface); _lockedKind = local.Y < PetSurface.ActualHeight * .35 ? "hair" : local.Y < PetSurface.ActualHeight * .78 ? "mouth" : "body";
            _interactionTargetX = 0; _interactionTargetY = 0;
            PlaySound(_pressSound);
            PetSurface.CaptureMouse();
            return;
        }
        var cursor = Forms.Cursor.Position;
        if (!GetWindowRect(WindowHandle, out var rect))
        {
            _mousePressed = false;
            return;
        }
        _dragStart = new System.Windows.Point(cursor.X, cursor.Y); _windowStartX = rect.Left; _windowStartY = rect.Top; _dragging = true; _dragMoved = false; PetSurface.CaptureMouse();
        PlaySound(_pressSound);
    }
    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _mousePressed = false;
        if (_settings.InteractionMode == "locked")
        {
            if (PetSurface.IsMouseCaptured) PetSurface.ReleaseMouseCapture();
            _lockedPressed = false; PlaySound(_releaseSound);
            if (_settings.InteractionEffects) KickReleaseBounce();
            var preserveVisualState = false;
            if (_lockedKind == "body") _ = RefreshAfterClickAsync();
            else preserveVisualState = ShowInteractionFeedback(_lockedKind);
            _interactionTargetX = 0; _interactionTargetY = 0;
            if (_lockedKind != "body" && _settings.InteractionEffects && !preserveVisualState) RestoreSteadyVisualState();
            return;
        }
        if (!_dragging) return;
        _dragging = false; if (PetSurface.IsMouseCaptured) PetSurface.ReleaseMouseCapture();
        SnapToEdge();
        if (_settings.InteractionEffects) KickReleaseBounce();
        PlaySound(_releaseSound); if (!_dragMoved) _ = RefreshAfterClickAsync(); else RestoreSteadyVisualState();
    }

    private void OnPetLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_mousePressed) return;
        _mousePressed = false;
        _dragging = false;
        _lockedPressed = false;
        _interactionTargetX = 0;
        _interactionTargetY = 0;
        if (_settings.InteractionEffects)
        {
            KickReleaseBounce();
            RestoreSteadyVisualState();
        }
    }

    private async Task RefreshAfterClickAsync()
    {
        if (_settings.InteractionEffects) await Task.Delay(420);
        if (!_closing) await RefreshAsync(true, true);
    }

    private void KickReleaseBounce()
    {
        StartSquashAnimation(0);
    }
    private void OnPetMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            var cursor = Forms.Cursor.Position;
            var point = new System.Windows.Point(cursor.X, cursor.Y);
            var dx = point.X - _dragStart.X; var dy = point.Y - _dragStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 4) _dragMoved = true;
            SetWindowPos(WindowHandle, IntPtr.Zero, _windowStartX + (int)Math.Round(dx), _windowStartY + (int)Math.Round(dy), 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
        else if (_settings.InteractionEffects && _lockedPressed && e.LeftButton == MouseButtonState.Pressed)
        {
            var point = PointToScreen(e.GetPosition(this)); var dx = Math.Clamp(point.X - _lockedStart.X, -45, 45); var dy = Math.Clamp(point.Y - _lockedStart.Y, -55, 35);
            _interactionTargetX = _lockedKind == "mouth" ? dx * .18 : _lockedKind == "hair" ? dx * .08 : 0;
            _interactionTargetY = _lockedKind == "hair" ? dy * .16 : 0;
        }
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync(true, true);

    private void OnPetContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        ApplyLocalization();
        BubbleMenuItem.Header = BubbleGroup.Visibility == Visibility.Visible
            ? AppLocalization.Text(_settings.Language, "隐藏气泡", "Hide bubble")
            : AppLocalization.Text(_settings.Language, "显示气泡", "Show bubble");
        InteractionMenuItem.Header = string.Equals(_settings.InteractionMode, "locked", StringComparison.OrdinalIgnoreCase)
            ? AppLocalization.Text(_settings.Language, "切换为自由拖动", "Switch to free drag")
            : AppLocalization.Text(_settings.Language, "切换为锁定互动", "Switch to locked interaction");
        UpdatePetStyleMenuChecks();
    }

    private async void OnContextRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync(true, true);

    private void OnContextBubbleClick(object sender, RoutedEventArgs e)
    {
        if (BubbleGroup.Visibility == Visibility.Visible)
        {
            HideBubble();
            return;
        }

        if (_hasBalance)
        {
            var selected = SelectedMonitor;
            var currency = selected?.Profile.Currency ?? _settings.Currency;
            var name = selected?.Profile.Name ?? "当前账户";
            ShowBubble("账户余额", $"{_lastBalance:0.00} {currency}", $"{name} · 今日已用 {_todayUsage:0.00} {currency}");
        }
        else
            ShowBubble("还没查询", "--", "点击立即刷新获取余额");
    }

    private void OnContextInteractionClick(object sender, RoutedEventArgs e)
    {
        _settings.InteractionMode = string.Equals(_settings.InteractionMode, "locked", StringComparison.OrdinalIgnoreCase) ? "free" : "locked";
        _settingsStore.Save(_settings);
        _lockedPressed = false;
        _interactionTargetX = 0;
        _interactionTargetY = 0;
        ShowBubble("交互模式", _settings.InteractionMode == "locked" ? "锁定互动" : "自由拖动", _settings.InteractionMode == "locked" ? "可以拽嘴角和提呆毛" : "按住角色即可移动");
    }

    private void OnPetStyleClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item) ChangePetStyle(item.Tag?.ToString() ?? "deepseek");
    }

    private void ChangePetStyle(string style)
    {
        var normalized = PetStyleCatalog.NormalizeId(style);
        if (!PetStyleCatalog.IsAvailable(normalized))
        {
            UpdatePetStyleAvailability();
            return;
        }
        if (string.Equals(_settings.PetStyle, normalized, StringComparison.OrdinalIgnoreCase))
        {
            UpdatePetStyleMenuChecks();
            return;
        }

        var previous = _settings.PetStyle;
        _settings.PetStyle = normalized;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _settings.PetStyle = previous;
            UpdatePetStyleMenuChecks();
            ShowBubble("切换失败", "未保存", "请检查 BalancePet 配置目录权限");
            return;
        }

        _activePetImagePath = null;
        LoadPetVisual(_visualState);
        ApplyFlipVisuals();
        EnsurePetTransforms();
        ResetInactiveTimer();
        UpdatePetStyleMenuChecks();
        ShowBubble("形象已切换", PetStyleDisplayName(normalized), "设置已保存", TimeSpan.FromSeconds(3.8));
    }

    private void UpdatePetStyleMenuChecks()
    {
        var style = NormalizePetStyle(_settings.PetStyle);
        DeepSeekStyleMenuItem.IsChecked = style == "deepseek";
        ChatGptStyleMenuItem.IsChecked = style == "chatgpt";
        MiniMaxStyleMenuItem.IsChecked = style == "minimax";
        GeminiStyleMenuItem.IsChecked = style == "gemini";
        GrokStyleMenuItem.IsChecked = style == "grok";
        if (_trayDeepSeekStyleItem is not null) _trayDeepSeekStyleItem.Checked = style == "deepseek";
        if (_trayChatGptStyleItem is not null) _trayChatGptStyleItem.Checked = style == "chatgpt";
        if (_trayMiniMaxStyleItem is not null) _trayMiniMaxStyleItem.Checked = style == "minimax";
        if (_trayGeminiStyleItem is not null) _trayGeminiStyleItem.Checked = style == "gemini";
        if (_trayGrokStyleItem is not null) _trayGrokStyleItem.Checked = style == "grok";
        foreach (var item in _trayStyleItems.Values) item.Checked = style == item.Tag?.ToString();
        UpdatePetStyleAvailability();
    }

    private void UpdatePetStyleAvailability()
    {
        RefreshExtensionStyleMenus();
        foreach (var item in ContextStyleMenuItem.Items.OfType<MenuItem>())
        {
            if (item.Tag is string id)
            {
                var available = PetStyleCatalog.IsAvailable(id);
                item.IsEnabled = available;
                item.ToolTip = available ? null : AppLocalization.Text(_settings.Language, "素材尚未完成", "Assets are not ready");
            }
        }

        foreach (var item in _trayStyleItems.Values)
        {
            if (item.Tag is string id)
                item.Enabled = PetStyleCatalog.IsAvailable(id);
        }
    }

    private void RefreshExtensionStyleMenus()
    {
        var extensionStyles = PetStyleCatalog.GetAvailableExtensionStyles();
        var availableExtensionIds = extensionStyles.Select(definition => definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInIds = PetStyleCatalog.All.Select(definition => definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ContextStyleMenuItem.Items.OfType<MenuItem>().ToArray())
        {
            if (item.Tag is string id && !builtInIds.Contains(id) && !availableExtensionIds.Contains(id))
                ContextStyleMenuItem.Items.Remove(item);
        }
        var contextIds = ContextStyleMenuItem.Items.OfType<MenuItem>()
            .Select(item => item.Tag?.ToString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in extensionStyles)
        {
            if (!contextIds.Add(definition.Id)) continue;
            var item = new MenuItem { Header = PetStyleDisplayName(definition.Id), Tag = definition.Id, IsCheckable = true };
            item.Click += OnPetStyleClick;
            ContextStyleMenuItem.Items.Add(item);
        }

        if (_trayStyleMenuItem is null) return;
        foreach (var item in _trayStyleItems.Where(pair => !builtInIds.Contains(pair.Key) && !availableExtensionIds.Contains(pair.Key)).ToArray())
        {
            _trayStyleMenuItem.DropDownItems.Remove(item.Value);
            _trayStyleItems.Remove(item.Key);
        }
        foreach (var definition in extensionStyles)
        {
            if (_trayStyleItems.ContainsKey(definition.Id)) continue;
            var item = new Forms.ToolStripMenuItem(PetStyleDisplayName(definition.Id)) { Tag = definition.Id };
            item.Click += (_, _) => ChangePetStyle(definition.Id);
            _trayStyleItems[definition.Id] = item;
            _trayStyleMenuItem.DropDownItems.Add(item);
        }
    }

    private void UpdateTrayMonitorMenu()
    {
        if (_trayMonitorMenu is null) return;
        _trayMonitorMenu.DropDownItems.Clear();
        _trayMonitorItems.Clear();
        foreach (var runtime in _monitorStates.Values.OrderBy(value => value.Profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new Forms.ToolStripMenuItem(runtime.Profile.Name)
            {
                Tag = runtime.Profile.Id,
                CheckOnClick = false,
                Checked = IsSelectedMonitor(runtime),
                Enabled = runtime.Profile.Enabled
            };
            item.Click += (_, _) => SelectMonitor(runtime.Profile.Id);
            _trayMonitorItems[runtime.Profile.Id] = item;
            _trayMonitorMenu.DropDownItems.Add(item);
        }
        if (_trayMonitorMenu.DropDownItems.Count == 0)
            _trayMonitorMenu.DropDownItems.Add(new Forms.ToolStripMenuItem(AppLocalization.Text(_settings.Language, "未配置账户", "No accounts configured")) { Enabled = false });
    }

    private void UpdateContextMonitorMenu()
    {
        MonitorMenuItem.Items.Clear();
        foreach (var runtime in _monitorStates.Values.OrderBy(value => value.Profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem
            {
                Header = runtime.Profile.Name,
                Tag = runtime.Profile.Id,
                IsCheckable = true,
                IsChecked = IsSelectedMonitor(runtime),
                IsEnabled = runtime.Profile.Enabled
            };
            item.Click += (_, _) => SelectMonitor(runtime.Profile.Id);
            MonitorMenuItem.Items.Add(item);
        }
        if (MonitorMenuItem.Items.Count == 0)
            MonitorMenuItem.Items.Add(new MenuItem { Header = AppLocalization.Text(_settings.Language, "未配置账户", "No accounts configured"), IsEnabled = false });
    }

    private void SelectMonitor(string profileId, bool announce = true, bool refreshWhenMissing = true)
    {
        if (!_monitorStates.ContainsKey(profileId)) return;
        _settings.SelectedMonitorId = profileId;
        SyncSelectedMonitorState();
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowBubble("切换失败", "未保存", "请检查 BalancePet 配置目录权限");
            return;
        }
        UpdateTrayMonitorMenu();
        UpdateContextMonitorMenu();
        RestoreSteadyVisualState();
        if (refreshWhenMissing && SelectedMonitor is { HasBalance: false }) _ = RefreshAsync(true, true);
        else if (announce) ShowBubble("已切换账户", SelectedMonitor?.Profile.Name ?? "当前账户", "余额与状态已切换");
    }

    private string PetStyleDisplayName(string style)
    {
        var definition = PetStyleCatalog.Get(style);
        return AppLocalization.Text(_settings.Language, definition.ChineseName, definition.EnglishName);
    }

    private void ApplyLocalization()
    {
        AppLocalization.Apply(this, _settings.Language);
        ContextRefreshMenuItem.Header = AppLocalization.Text(_settings.Language, "立即刷新", "Refresh now");
        ContextStyleMenuItem.Header = AppLocalization.Text(_settings.Language, "切换形象", "Change appearance");
        MonitorMenuItem.Header = AppLocalization.Text(_settings.Language, "当前账户", "Current account");
        ContextSettingsMenuItem.Header = AppLocalization.Text(_settings.Language, "配置接口", "Configure API");
        ContextUpdateMenuItem.Header = AppLocalization.Text(_settings.Language, "检查更新", "Check for updates");
        ContextUsageMenuItem.Header = AppLocalization.Text(_settings.Language, "用量统计", "Usage");
        ContextHideMenuItem.Header = AppLocalization.Text(_settings.Language, "隐藏桌宠", "Hide pet");
        ContextExitMenuItem.Header = AppLocalization.Text(_settings.Language, "退出", "Exit");
        DeepSeekStyleMenuItem.Header = PetStyleDisplayName("deepseek");
        ChatGptStyleMenuItem.Header = PetStyleDisplayName("chatgpt");
        MiniMaxStyleMenuItem.Header = PetStyleDisplayName("minimax");
        GeminiStyleMenuItem.Header = PetStyleDisplayName("gemini");
        GrokStyleMenuItem.Header = PetStyleDisplayName("grok");
        foreach (var item in ContextStyleMenuItem.Items.OfType<MenuItem>())
            if (item.Tag is string id) item.Header = PetStyleDisplayName(id);
        if (_trayShowItem is not null) _trayShowItem.Text = AppLocalization.Text(_settings.Language, "显示桌宠", "Show pet");
        if (_trayRefreshItem is not null) _trayRefreshItem.Text = AppLocalization.Text(_settings.Language, "立即刷新", "Refresh now");
        if (_traySettingsItem is not null) _traySettingsItem.Text = AppLocalization.Text(_settings.Language, "配置接口", "Configure API");
        if (_trayStyleMenuItem is not null) _trayStyleMenuItem.Text = AppLocalization.Text(_settings.Language, "切换形象", "Change appearance");
        if (_trayMonitorMenu is not null) _trayMonitorMenu.Text = AppLocalization.Text(_settings.Language, "当前账户", "Current account");
        if (_trayUpdateItem is not null) _trayUpdateItem.Text = AppLocalization.Text(_settings.Language, "检查更新", "Check for updates");
        if (_trayUsageItem is not null) _trayUsageItem.Text = AppLocalization.Text(_settings.Language, "用量统计", "Usage");
        if (_trayExitItem is not null) _trayExitItem.Text = AppLocalization.Text(_settings.Language, "退出", "Exit");
        if (_trayDeepSeekStyleItem is not null) _trayDeepSeekStyleItem.Text = PetStyleDisplayName("deepseek");
        if (_trayChatGptStyleItem is not null) _trayChatGptStyleItem.Text = PetStyleDisplayName("chatgpt");
        if (_trayMiniMaxStyleItem is not null) _trayMiniMaxStyleItem.Text = PetStyleDisplayName("minimax");
        if (_trayGeminiStyleItem is not null) _trayGeminiStyleItem.Text = PetStyleDisplayName("gemini");
        if (_trayGrokStyleItem is not null) _trayGrokStyleItem.Text = PetStyleDisplayName("grok");
        foreach (var item in _trayStyleItems.Values)
            if (item.Tag is string id) item.Text = PetStyleDisplayName(id);
        if (_trayIcon is not null) _trayIcon.Text = AppLocalization.Text(_settings.Language, "小余额", "BalancePet");
    }

    private void OnContextSettingsClick(object sender, RoutedEventArgs e) => OnSettingsClick(this, new RoutedEventArgs());

    private async void OnContextUpdateClick(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private void OnContextUsageClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(OpenUsageWindow));

    private void OnContextHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnContextExitClick(object sender, RoutedEventArgs e)
    {
        _closing = true;
        System.Windows.Application.Current.Shutdown();
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var refreshTimerWasEnabled = _refreshTimer.IsEnabled;
        _refreshTimer.Stop();
        await StopActiveRefreshAsync();
        try
        {
            var dialog = new SettingsWindow(_settingsStore, _tokenStore, _settings) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                LoadSettingsAndPosition();
                ConfigureSounds();
                await RefreshAsync(true, true);
            }
        }
        finally
        {
            if (!_closing && refreshTimerWasEnabled) ConfigureRefreshTimer();
        }
    }

    private async Task StopActiveRefreshAsync()
    {
        _refreshCancellation?.Cancel();
        while (_refreshing)
            await Task.Delay(25);
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void SnapToEdge()
    {
        if (!_dragMoved) { SavePosition(); return; }
        if (!GetWindowRect(WindowHandle, out var rect)) { SavePosition(); return; }
        var monitor = MonitorFromWindow(WindowHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) { SavePosition(); return; }
        var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
        var x = rect.Left; var y = rect.Top;
        var snappedLeft = false;
        var snappedHorizontal = false;
        if (rect.Left <= info.Work.Left + EdgeSnapDistance)
        {
            x = info.Work.Left;
            snappedLeft = true;
            snappedHorizontal = true;
        }
        else if (rect.Right >= info.Work.Right - EdgeSnapDistance)
        {
            x = info.Work.Right - width;
            snappedHorizontal = true;
        }
        if (rect.Top <= info.Work.Top + EdgeSnapDistance)
            y = info.Work.Top;
        else if (rect.Bottom >= info.Work.Bottom - EdgeSnapDistance)
            y = info.Work.Bottom - height;
        x = Math.Clamp(x, info.Work.Left, info.Work.Right - width);
        y = Math.Clamp(y, info.Work.Top, info.Work.Bottom - height);
        if (snappedHorizontal)
        {
            _settings.Flipped = snappedLeft;
            ApplyFlipVisuals();
        }
        SetWindowPos(WindowHandle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        Dispatcher.BeginInvoke(SavePosition, DispatcherPriority.ApplicationIdle);
    }

    private void SetupTray()
    {
        if (_trayIcon is not null) return;
        _trayImage = LoadTrayIcon();
        var menu = new Forms.ContextMenuStrip
        {
            AutoSize = true,
            AutoClose = true,
            ShowImageMargin = false,
            ShowCheckMargin = true,
            ShowItemToolTips = false,
            RenderMode = Forms.ToolStripRenderMode.System,
            Margin = new System.Windows.Forms.Padding(0),
            Padding = new System.Windows.Forms.Padding(0)
        };
        _trayShowItem = new Forms.ToolStripMenuItem();
        _trayShowItem.Click += (_, _) => { Show(); Activate(); };
        menu.Items.Add(_trayShowItem);
        _trayRefreshItem = new Forms.ToolStripMenuItem();
        _trayRefreshItem.Click += async (_, _) => await RefreshAsync(true, true);
        menu.Items.Add(_trayRefreshItem);
        _traySettingsItem = new Forms.ToolStripMenuItem();
        _traySettingsItem.Click += (_, _) => OnSettingsClick(this, new RoutedEventArgs());
        menu.Items.Add(_traySettingsItem);
        var styleMenu = new Forms.ToolStripMenuItem();
        _trayStyleMenuItem = styleMenu;
        _trayDeepSeekStyleItem = new Forms.ToolStripMenuItem(PetStyleDisplayName("deepseek")) { Tag = "deepseek" };
        _trayChatGptStyleItem = new Forms.ToolStripMenuItem(PetStyleDisplayName("chatgpt")) { Tag = "chatgpt" };
        _trayMiniMaxStyleItem = new Forms.ToolStripMenuItem(PetStyleDisplayName("minimax")) { Tag = "minimax" };
        _trayGeminiStyleItem = new Forms.ToolStripMenuItem(PetStyleDisplayName("gemini")) { Tag = "gemini" };
        _trayGrokStyleItem = new Forms.ToolStripMenuItem(PetStyleDisplayName("grok")) { Tag = "grok" };
        _trayDeepSeekStyleItem.Click += (_, _) => ChangePetStyle("deepseek");
        _trayChatGptStyleItem.Click += (_, _) => ChangePetStyle("chatgpt");
        _trayMiniMaxStyleItem.Click += (_, _) => ChangePetStyle("minimax");
        _trayGeminiStyleItem.Click += (_, _) => ChangePetStyle("gemini");
        _trayGrokStyleItem.Click += (_, _) => ChangePetStyle("grok");
        _trayStyleItems.Clear();
        _trayStyleItems["deepseek"] = _trayDeepSeekStyleItem;
        _trayStyleItems["chatgpt"] = _trayChatGptStyleItem;
        _trayStyleItems["minimax"] = _trayMiniMaxStyleItem;
        _trayStyleItems["gemini"] = _trayGeminiStyleItem;
        _trayStyleItems["grok"] = _trayGrokStyleItem;
        foreach (var definition in PetStyleCatalog.All)
        {
            if (definition.Id is "deepseek" or "chatgpt" or "minimax" or "gemini" or "grok") continue;
            var item = new Forms.ToolStripMenuItem(PetStyleDisplayName(definition.Id)) { Tag = definition.Id };
            item.Click += (_, _) => ChangePetStyle(definition.Id);
            _trayStyleItems[definition.Id] = item;
        }
        styleMenu.DropDownItems.Add(_trayDeepSeekStyleItem);
        styleMenu.DropDownItems.Add(_trayChatGptStyleItem);
        styleMenu.DropDownItems.Add(_trayMiniMaxStyleItem);
        styleMenu.DropDownItems.Add(_trayGeminiStyleItem);
        styleMenu.DropDownItems.Add(_trayGrokStyleItem);
        styleMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var item in _trayStyleItems.Values) styleMenu.DropDownItems.Add(item);
        menu.Items.Add(styleMenu);
        _trayMonitorMenu = new Forms.ToolStripMenuItem();
        menu.Items.Add(_trayMonitorMenu);
        _trayUpdateItem = new Forms.ToolStripMenuItem();
        _trayUpdateItem.Click += (_, _) => _ = CheckForUpdatesAsync(true);
        menu.Items.Add(_trayUpdateItem);
        _trayUsageItem = new Forms.ToolStripMenuItem();
        _trayUsageItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(OpenUsageWindow));
        menu.Items.Add(_trayUsageItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _trayExitItem = new Forms.ToolStripMenuItem();
        _trayExitItem.Click += (_, _) => { _closing = true; System.Windows.Application.Current.Shutdown(); };
        menu.Items.Add(_trayExitItem);
        _trayMenu = menu;
        // Assign the icon before making the shell icon visible. Some Explorer
        // versions do not repaint a NotifyIcon that started with Icon == null.
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "BalancePet",
            Icon = _trayImage,
            ContextMenuStrip = menu,
            Visible = true
        };
        ApplyLocalization();
        UpdatePetStyleMenuChecks();
        _trayIcon.DoubleClick += (_, _) => { Show(); Activate(); };
        // Explorer may not have created the notification area yet during a
        // Windows logon launch. Re-register the icon a few times so startup
        // does not require the user to quit and reopen the app.
        _trayRecoveryAttempts = 0;
        _trayRecoveryTimer.Stop();
        _trayRecoveryTimer.Start();
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_closing || _updateBusy) return;
        _updateBusy = true;
        try
        {
            var current = GetCurrentVersion();
            var release = await _updateService.CheckAsync(current);
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settingsStore.Save(_settings);
            if (release is null)
            {
                if (manual) ShowBubble("已是最新版本", current, "当前不需要更新");
                return;
            }

            if (!UpdateInstaller.TryCreatePlan(release, AppContext.BaseDirectory, out var plan, out var planError) || plan is null)
                throw new InvalidOperationException(planError);

            var dialog = new UpdateWindow(release, plan, _settings.Language) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            ShowBubble("正在更新", release.TagName, "下载并校验中");
            var payload = await _updateService.DownloadAsync(plan.Asset);
            if (plan.Method == UpdateInstallMethod.Installer)
            {
                if (!UpdateInstaller.TryLaunchInstaller(payload, AppContext.BaseDirectory, out var installerError))
                {
                    try { File.Delete(payload); } catch (IOException) { }
                    throw new InvalidOperationException(installerError);
                }

                ShowBubble("安装器已启动", release.TagName, "请在安装器中确认管理员授权并完成更新");
                return;
            }

            if (!UpdateInstaller.TryLaunchArchive(payload, AppContext.BaseDirectory, Environment.ProcessId, release.TagName, out var error))
            {
                try { File.Delete(payload); } catch (IOException) { }
                throw new InvalidOperationException(error);
            }
            _closing = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            if (manual) System.Windows.MessageBox.Show(this, $"检查更新失败：{error.Message}", "BalancePet 更新", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _updateBusy = false; }
    }

    private void ConfigureUpdateChecks()
    {
        if (string.Equals(_configuredUpdateCheckMode, _settings.UpdateCheckMode, StringComparison.Ordinal)) return;
        _updateTimer.Stop();
        switch (_settings.UpdateCheckMode)
        {
            case "manual":
                _configuredUpdateCheckMode = "manual";
                return;
            case "startup":
                _configuredUpdateCheckMode = "startup";
                _ = CheckForUpdatesAsync(false);
                return;
            case "weekly":
            case "daily":
                _configuredUpdateCheckMode = _settings.UpdateCheckMode;
                _updateTimer.Start();
                _ = CheckForAutomaticUpdatesAsync();
                return;
            default:
                _settings.UpdateCheckMode = "daily";
                _configuredUpdateCheckMode = "daily";
                _settingsStore.Save(_settings);
                _updateTimer.Start();
                _ = CheckForAutomaticUpdatesAsync();
                return;
        }
    }

    private async Task CheckForAutomaticUpdatesAsync()
    {
        var interval = _settings.UpdateCheckMode == "weekly" ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        if (_settings.LastUpdateCheckUtc.HasValue && DateTimeOffset.UtcNow - _settings.LastUpdateCheckUtc.Value < interval) return;
        await CheckForUpdatesAsync(false);
    }

    private static string GetCurrentVersion()
    {
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? "0.1.0-beta.0" : informational.Split('+')[0];
    }

    private bool ShowPostUpdateConfirmation()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(arguments, argument => string.Equals(argument, "--updated-to", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= arguments.Length) return false;

        var version = arguments[index + 1].Trim();
        if (string.IsNullOrWhiteSpace(version) || version.Length > 64) return false;
        if (!IsVisible)
        {
            _temporarilyShownForUpdate = _settings.CodexTaskIntegration && !IsVisible;
            Show();
        }
        SetVisualState(PetVisualState.Success, 3600);
        ShowBubble("更新完成", version, "BalancePet 已重新启动", TimeSpan.FromSeconds(6));
        ShowSystemNotification("BalancePet 已更新", $"当前版本 {version}", Forms.ToolTipIcon.Info);
        if (_temporarilyShownForUpdate)
        {
            _codexHideTimer.Stop();
            _codexHideTimer.Start();
        }
        return true;
    }

    private void RecoverTrayRegistration()
    {
        if (_closing || _trayIcon is null)
        {
            _trayRecoveryTimer.Stop();
            return;
        }

        _trayRecoveryAttempts++;
        ReRegisterTrayIcon();

        if (_trayRecoveryAttempts >= 4) _trayRecoveryTimer.Stop();
    }

    private void RegisterTaskbarCreatedHook()
    {
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0) return;
        _windowSource = HwndSource.FromHwnd(WindowHandle);
        _windowSource?.AddHook(OnWindowMessage);
    }

    private void HideFromTaskSwitcher()
    {
        var handle = WindowHandle;
        if (handle == IntPtr.Zero) return;

        var current = GetExtendedWindowStyle(handle);
        var updated = (current | WsExToolWindow) & ~WsExAppWindow;
        if (updated == current) return;

        SetExtendedWindowStyle(handle, updated);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private static long GetExtendedWindowStyle(IntPtr handle) => IntPtr.Size == 8
        ? GetWindowLongPtr64(handle, GwlExStyle).ToInt64()
        : GetWindowLong32(handle, GwlExStyle);

    private static void SetExtendedWindowStyle(IntPtr handle, long value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(handle, GwlExStyle, new IntPtr(value));
        else
            SetWindowLong32(handle, GwlExStyle, unchecked((int)value));
    }

    private void UnregisterTaskbarCreatedHook()
    {
        if (_windowSource is null) return;
        _windowSource.RemoveHook(OnWindowMessage);
        _windowSource = null;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReRegisterTrayIcon));
        }
        return IntPtr.Zero;
    }

    private void ReRegisterTrayIcon()
    {
        if (_closing || _trayIcon is null) return;
        try
        {
            // Toggling Visible forces NotifyIcon to send a fresh add request
            // to Explorer after the shell notification area is ready.
            _trayIcon.Visible = false;
            _trayIcon.Icon = _trayImage;
            _trayIcon.Visible = true;
        }
        catch (ObjectDisposedException)
        {
            _trayRecoveryTimer.Stop();
        }
        catch (InvalidOperationException)
        {
            // A transient shell/WinForms state can be retried by the timer.
        }
        catch (ArgumentException)
        {
            // Keep the app alive if Explorer rejects an icon handle at logon.
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "balance-pet.ico");
        try
        {
            if (File.Exists(iconPath)) return new System.Drawing.Icon(iconPath);
        }
        catch (Exception)
        {
            // Fall through to the executable/system icon if an unpacked asset
            // is missing or an older ICO decoder rejects the file.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                var executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (executableIcon is not null) return executableIcon;
            }
        }
        catch (Exception)
        {
            // Use the guaranteed system fallback below.
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private void DisposeTray()
    {
        var tray = _trayIcon;
        _trayIcon = null;
        if (tray is not null)
        {
            tray.Visible = false;
            tray.ContextMenuStrip = null;
            tray.Dispose();
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayDeepSeekStyleItem = null;
        _trayChatGptStyleItem = null;
        _trayMiniMaxStyleItem = null;
        _trayGeminiStyleItem = null;
        _trayGrokStyleItem = null;
        _trayStyleItems.Clear();
        _trayMonitorMenu = null;
        _trayShowItem = null;
        _trayRefreshItem = null;
        _traySettingsItem = null;
        _trayUpdateItem = null;
        _trayUsageItem = null;
        _trayExitItem = null;
        _trayStyleMenuItem = null;
        _trayMonitorItems.Clear();
        _trayImage?.Dispose();
        _trayImage = null;
    }

    private void OpenUsageWindow()
    {
        var dialog = new UsageWindow(SelectedMonitor?.UsageStore ?? _usageStore, _settings.Language) { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowBubble(string label, string amount, string hint, TimeSpan? duration = null)
    {
        if (!_settings.Bubble) return;
        label = AppLocalization.Translate(label, _settings.Language);
        amount = AppLocalization.Translate(amount, _settings.Language);
        hint = AppLocalization.Translate(hint, _settings.Language);
        _pendingBubbleLabel = label;
        _pendingBubbleAmount = amount;
        _pendingBubbleHint = hint;
        if (BubbleGroup.Visibility != Visibility.Visible)
        {
            SetBubbleText(label, amount, hint);
            BubbleContent.Opacity = 1;
            BubbleGroup.Visibility = Visibility.Visible;
            _bubbleAnimationProgress = 0;
            _bubbleAnimationFrom = 0;
            _bubbleAnimationTo = 1;
            _bubbleOpening = true;
            _bubbleAnimationTimer.Start();
        }
        else
        {
            BubbleContent.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            _bubbleContentTimer.Stop();
            _bubbleContentTimer.Start();
        }
        _bubbleTimer.Stop();
        _bubbleTimer.Interval = duration ?? TimeSpan.FromSeconds(hint.Length > 36 ? 7 : 5.5);
        _bubbleTimer.Start();
    }
    private void HideBubble()
    {
        _bubbleTimer.Stop();
        if (BubbleGroup.Visibility != Visibility.Visible) return;
        _bubbleAnimationProgress = 0;
        _bubbleAnimationFrom = 1;
        _bubbleAnimationTo = 0;
        _bubbleOpening = false;
        _bubbleAnimationTimer.Start();
    }

    private void SetBubbleText(string label, string amount, string hint)
    {
        BubbleLabel.Text = label;
        BubbleLabel.MaxWidth = BubbleTextMaxWidth;
        BubbleLabel.TextWrapping = TextWrapping.Wrap;
        BubbleLabel.TextTrimming = TextTrimming.None;
        SetBubbleAmountText(amount);
        BubbleHint.Text = hint;
    }

    private void SetBubbleAmountText(string amount)
    {
        BubbleAmount.Text = amount;
        BubbleAmount.Width = double.NaN;
        BubbleAmount.MaxWidth = double.PositiveInfinity;
        BubbleAmount.TextWrapping = TextWrapping.NoWrap;
        BubbleAmount.TextTrimming = TextTrimming.None;

        var fittedSize = GetBubbleAmountFontSize(amount);
        for (var size = fittedSize; size >= 14; size -= 0.5)
        {
            BubbleAmount.FontSize = size;
            BubbleAmount.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            if (BubbleAmount.DesiredSize.Width <= BubbleTextMaxWidth)
            {
                fittedSize = size;
                break;
            }
            fittedSize = size;
        }

        BubbleAmount.FontSize = fittedSize;
        BubbleAmount.MaxWidth = BubbleTextMaxWidth;
        BubbleAmount.Width = BubbleTextMaxWidth;
        BubbleAmount.TextWrapping = TextWrapping.Wrap;
    }

    private static double GetBubbleAmountFontSize(string amount)
    {
        return amount.Length switch
        {
            <= 9 => 39,
            <= 14 => 30,
            <= 18 => 25,
            <= 24 => 21,
            _ => 18
        };
    }

    private void OnBubbleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ResetInactiveTimer();
        var lines = NormalizePetStyle(_settings.PetStyle) switch
        {
            "chatgpt" => new[]
            {
                ("霁珑在看着", "放心吧", "余额变动会告诉你"),
                ("龙角有点痒", "轻一点", "点击角色可以刷新余额"),
                ("慢慢来", "不用着急", "需要时我会在这里"),
                ("工作继续", "保持专注", "完成后会显示本次消耗")
            },
            "minimax" => new[]
            {
                ("绯音在看着", "放心吧", "余额变动会告诉你"),
                ("耳坠晃了一下", "轻一点", "点击角色可以刷新余额"),
                ("慢慢来", "不用着急", "需要时我会在这里"),
                ("工作继续", "保持专注", "完成后会显示本次消耗")
            },
            "gemini" => new[]
            {
                ("星璃在看着", "放心吧", "余额变动会告诉你"),
                ("星星耳饰闪了一下", "轻一点", "点击角色可以刷新余额"),
                ("慢慢来", "不用着急", "需要时我会在这里"),
                ("工作继续", "保持专注", "完成后会显示本次消耗")
            },
            "grok" => new[]
            {
                ("烬斧在看着", "放心吧", "余额变动会告诉你"),
                ("蝙蝠发饰晃了一下", "轻一点", "点击角色可以刷新余额"),
                ("慢慢来", "不用着急", "需要时我会在这里"),
                ("工作继续", "保持专注", "完成后会显示本次消耗")
            },
            _ => new[]
            {
                ("澜汐在看着", "放心吧", "余额变动会告诉你"),
                ("耳鳍动了一下", "轻一点", "点击角色可以刷新余额"),
                ("慢慢来", "不用着急", "需要时我会在这里"),
                ("工作继续", "保持专注", "完成后会显示本次消耗")
            }
        };
        var line = lines[Random.Shared.Next(lines.Length)];
        ShowBubble(line.Item1, line.Item2, line.Item3, TimeSpan.FromSeconds(4.5));
    }

    private bool ShowInteractionFeedback(string kind)
    {
        var now = DateTimeOffset.UtcNow;
        _interactionStreak = now - _lastInteractionAt <= TimeSpan.FromSeconds(10) ? _interactionStreak + 1 : 1;
        _lastInteractionAt = now;

        var special = _settings.RandomEasterEggs && _interactionStreak >= 4;
        if (special)
        {
            _interactionStreak = 0;
            if (_settings.InteractionEffects) SetVisualState(PetVisualState.Success, 1100);
            var surprise = NormalizePetStyle(_settings.PetStyle) switch
            {
                "chatgpt" => ("被发现了", "霁珑笑了一下", "连续互动彩蛋"),
                "minimax" => ("被发现了", "绯音眨了眨眼", "连续互动彩蛋"),
                "gemini" => ("被发现了", "星璃眨了眨眼", "连续互动彩蛋"),
                "grok" => ("被发现了", "烬斧露出了小尖牙", "连续互动彩蛋"),
                _ => ("被发现了", "澜汐眨了眨眼", "连续互动彩蛋")
            };
            ShowBubble(surprise.Item1, surprise.Item2, surprise.Item3, TimeSpan.FromSeconds(4.2));
            return _settings.InteractionEffects;
        }

        var lines = GetInteractionLines(kind);
        var line = lines[Random.Shared.Next(lines.Length)];
        ShowBubble(line.Label, line.Amount, line.Hint, TimeSpan.FromSeconds(3.8));
        return false;
    }

    private (string Label, string Amount, string Hint)[] GetInteractionLines(string kind)
    {
        var style = NormalizePetStyle(_settings.PetStyle);
        if (style == "chatgpt")
        {
            return kind switch
            {
                "hair" => new[] { ("龙角被碰到", "有点痒", "霁珑轻轻躲开了"), ("不要戳角角", "哎呀", "发型会乱掉的") },
                "mouth" => new[] { ("脸颊被碰到", "唔", "霁珑有点害羞"), ("轻一点嘛", "在呢", "我会继续看着余额") },
                _ => new[] { ("被戳到了", "在呢", "点击可以刷新余额") }
            };
        }

        if (style == "minimax")
        {
            return kind switch
            {
                "hair" => new[] { ("发梢被碰到", "有点痒", "绯音轻轻躲开了"), ("不要拽头发", "轻一点", "发型会乱掉的") },
                "mouth" => new[] { ("脸颊被碰到", "唔", "绯音有点害羞"), ("轻一点嘛", "在呢", "我会继续看着余额") },
                _ => new[] { ("被戳到了", "在呢", "点击可以刷新余额") }
            };
        }

        if (style == "gemini")
        {
            return kind switch
            {
                "hair" => new[] { ("耳朵被碰到", "有点痒", "星璃轻轻晃了晃耳朵"), ("不要戳耳朵", "轻一点", "星星耳饰都要摇晃了") },
                "mouth" => new[] { ("脸颊被碰到", "唔", "星璃有点害羞"), ("轻一点嘛", "在呢", "我会继续看着余额") },
                _ => new[] { ("被戳到了", "在呢", "点击可以刷新余额") }
            };
        }

        if (style == "grok")
        {
            return kind switch
            {
                "hair" => new[] { ("发饰被碰到", "有点痒", "烬斧轻轻晃了晃蝙蝠发饰"), ("不要戳发饰", "轻一点", "小心她的 X 形武器") },
                "mouth" => new[] { ("脸颊被碰到", "唔", "烬斧有点害羞"), ("轻一点嘛", "在呢", "我会继续看着余额") },
                _ => new[] { ("被戳到了", "在呢", "点击可以刷新余额") }
            };
        }

        return kind switch
        {
            "hair" => new[] { ("呆毛被提起", "哎呀", "澜汐的发型要乱啦"), ("不要拽呆毛", "轻一点", "会痒的") },
            "mouth" => new[] { ("脸颊被碰到", "唔", "澜汐有点害羞"), ("轻一点嘛", "在呢", "我会继续看着余额") },
            _ => new[] { ("被戳到了", "在呢", "点击可以刷新余额") }
        };
    }

    private static string NormalizePetStyle(string? style)
    {
        var normalized = PetStyleCatalog.NormalizeId(style);
        return PetStyleCatalog.IsAvailable(normalized) ? normalized : "deepseek";
    }

    private bool IsDragonStyle() => NormalizePetStyle(_settings.PetStyle) == "chatgpt";

    private void AnimateBubble()
    {
        _bubbleAnimationProgress = Math.Min(1, _bubbleAnimationProgress + 0.016 / 0.32);
        var eased = OvershootCubicBezier(_bubbleAnimationProgress, .34, 1.56, .64, 1);
        var value = _bubbleAnimationFrom + (_bubbleAnimationTo - _bubbleAnimationFrom) * eased;
        BubbleGroup.Opacity = value;
        BubbleScale.ScaleX = .76 + .24 * value;
        BubbleScale.ScaleY = .76 + .24 * value;
        BubbleTranslate.Y = 16 * (1 - value);
        if (_bubbleAnimationProgress >= 1)
        {
            _bubbleAnimationTimer.Stop();
            if (!_bubbleOpening)
            {
                BubbleGroup.Visibility = Visibility.Collapsed;
                BubbleGroup.Opacity = 0;
            }
        }
    }

    private void AnimateBubbleContent()
    {
        _bubbleContentTimer.Stop();
        SetBubbleText(_pendingBubbleLabel, _pendingBubbleAmount, _pendingBubbleHint);
        BubbleContent.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void AnimateAmountTo(double amount, string currency)
    {
        _amountFrom = _hasDisplayAmount ? _displayBalance : amount;
        _amountTo = amount;
        _amountCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency;
        _amountAnimationProgress = 0;
        _hasDisplayAmount = true;
        _amountAnimationTimer.Start();
    }

    private void AnimateAmount()
    {
        _amountAnimationProgress = Math.Min(1, _amountAnimationProgress + 0.016 / 0.72);
        var eased = 1 - Math.Pow(1 - _amountAnimationProgress, 3);
        _displayBalance = _amountFrom + (_amountTo - _amountFrom) * eased;
        if (BubbleGroup.Visibility == Visibility.Visible && BubbleLabel.Text.Contains("余额", StringComparison.Ordinal))
            SetBubbleAmountText($"{_displayBalance:0.00} {_amountCurrency}");
        if (_amountAnimationProgress >= 1) _amountAnimationTimer.Stop();
    }
    private void AnimatePet()
    {
        EnsurePetTransforms();
        var now = Environment.TickCount64;
        if (_lastAnimationTick == 0) _lastAnimationTick = now;
        var elapsed = Math.Clamp((now - _lastAnimationTick) / 1000.0, 0.008, 0.05);
        _lastAnimationTick = now;
        if (_settings.InteractionEffects)
        {
            UpdateSquashAnimation(elapsed);
            _interactionX += (_interactionTargetX - _interactionX) * (1 - Math.Exp(-elapsed * 16));
            _interactionY += (_interactionTargetY - _interactionY) * (1 - Math.Exp(-elapsed * 16));
        }
        else
        {
            _squashAnimating = false;
            _squashProgress = 0;
            _interactionX = 0;
            _interactionY = 0;
            _interactionTargetX = 0;
            _interactionTargetY = 0;
        }
        var squashX = 1 + _squashProgress * .05;
        var squashY = 1 - _squashProgress * .12;
        var tilt = 0d;
        if (_settings.InteractionEffects && _lockedKind == "hair") tilt += Math.Clamp(_interactionX * .12, -5.5, 5.5);
        else if (_settings.InteractionEffects && _lockedKind == "mouth") tilt += Math.Clamp(_interactionX * .045, -2.5, 2.5);
        _petTranslate.X = _interactionX;
        _petTranslate.Y = _interactionY;
        _petScale.ScaleX = squashX * (_settings.Flipped ? -1 : 1);
        _petScale.ScaleY = squashY;
        _petRotate.Angle = tilt;
    }

    private void StartSquashAnimation(double target)
    {
        if (!_settings.InteractionEffects) return;
        _squashFrom = _squashProgress;
        _squashTo = target;
        _squashClock = 0;
        _squashAnimating = true;
    }

    private void UpdateSquashAnimation(double elapsed)
    {
        if (!_squashAnimating) return;
        _squashClock += elapsed;
        var progress = Math.Clamp(_squashClock / .22, 0, 1);
        var eased = OvershootCubicBezier(progress, .34, 1.56, .64, 1.0);
        _squashProgress = _squashFrom + (_squashTo - _squashFrom) * eased;
        if (progress >= 1)
        {
            _squashProgress = _squashTo;
            _squashAnimating = false;
        }
    }

    // Evaluates an overshooting cubic-bezier easing curve.
    private static double OvershootCubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        var low = 0d;
        var high = 1d;
        for (var i = 0; i < 16; i++)
        {
            var t = (low + high) / 2;
            if (Cubic(t, x1, x2) < x) low = t; else high = t;
        }
        return Cubic((low + high) / 2, y1, y2);
    }

    private static double Cubic(double t, double p1, double p2)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * t * p1 + 3 * inverse * t * t * p2 + t * t * t;
    }

    private void ConfigureSounds()
    {
        try
        {
            var pressPath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "press.mp3");
            var releasePath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "release.mp3");
            if (File.Exists(pressPath)) { _pressSound.Stop(); _pressSound.Open(new Uri(pressPath)); }
            if (File.Exists(releasePath)) { _releaseSound.Stop(); _releaseSound.Open(new Uri(releasePath)); }
            _pressSound.Volume = Math.Clamp(_settings.Volume, 0, 1); _releaseSound.Volume = Math.Clamp(_settings.Volume, 0, 1);
        }
        catch (InvalidOperationException) { }
    }

    private void PlaySound(System.Windows.Media.MediaPlayer player)
    {
        if (!_settings.Sound) return;
        try { player.Stop(); player.Position = TimeSpan.Zero; player.Play(); } catch (InvalidOperationException) { }
    }

    private void OnCodexTaskActivityReceived(object? sender, CodexTaskActivity activity)
    {
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (!_settings.CodexTaskIntegration || _closing) return;
            if (activity.State == "start") await StartCodexTaskAsync(activity);
            else await CompleteCodexTaskAsync(activity);
        }));
    }

    private void OnAccountStatusReceived(object? sender, AiAccountActivity activity)
    {
        Dispatcher.BeginInvoke(new Action(() => HandleAccountStatus(activity)));
    }

    private void HandleAccountStatus(AiAccountActivity activity)
    {
        if (_closing || !_settings.AccountStatusIntegration) return;
        var key = $"{activity.State}|{activity.Provider}|{activity.AccountType}|{activity.AccountLabel}|{activity.Endpoint}|{activity.TokenFingerprint}";
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(key, _lastAccountStatusKey, StringComparison.Ordinal) && now - _lastAccountStatusAt < TimeSpan.FromSeconds(2)) return;
        _lastAccountStatusKey = key;
        _lastAccountStatusAt = now;

        if (activity.State == "logout")
        {
            ShowBubble($"{activity.Provider} 已退出", "登录状态已更新", "重新登录后会再次提示");
            return;
        }

        var label = string.IsNullOrWhiteSpace(activity.AccountLabel) ? "" : $" · {activity.AccountLabel}";
        var accountType = AccountSourceClassifier.ResolveAccountType(activity);
        if (accountType == "official")
        {
            ShowBubble($"{activity.Provider} 官方账户已登录", "登录成功", $"官方账户{label}");
            return;
        }
        else if (accountType == "official-api")
        {
            var amount = activity.ReportedBalance.HasValue
                ? FormatAccountBalance(activity.ReportedBalance.Value, activity.Currency)
                : "余额未提供";
            ShowBubble($"{activity.Provider} API 已登录", amount, $"官方 API{label}");
            return;
        }

        var local = FindMatchingMonitor(activity);
        if (local is not null)
        {
            if (!IsSelectedMonitor(local)) SelectMonitor(local.Profile.Id, announce: false, refreshWhenMissing: false);
            var balance = local.LastBalance.HasValue
                ? FormatAccountBalance(local.LastBalance.Value, local.Profile.Currency)
                : "余额待查询";
            ShowBubble(
                $"{local.Profile.Name} API 已登录",
                balance,
                $"已匹配本地账户 · {local.Profile.Name}");
            _ = RefreshAsync(true, true, true);
            return;
        }

        if (accountType is "relay-api" or "third-party")
        {
            const string reminder = "是否保存到 BalancePet？右键桌宠打开“配置接口”";
            ShowBubble("第三方 API 已登录", "未匹配本地账户", reminder);
            ShowSystemNotification("未收录的第三方 API", reminder, Forms.ToolTipIcon.Info);
        }
        else
        {
            ShowBubble($"{activity.Provider} 账户已登录", "登录成功", $"来源未分类{label}");
        }
    }

    private MonitorRuntime? FindMatchingMonitor(AiAccountActivity activity)
    {
        var enabled = _monitorStates.Values.Where(runtime => runtime.Profile.Enabled).ToArray();
        if (activity.TokenFingerprint.Length == 64)
        {
            var exact = enabled.FirstOrDefault(runtime => string.Equals(runtime.TokenFingerprint, activity.TokenFingerprint, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        if (string.IsNullOrWhiteSpace(activity.Endpoint)) return null;
        var endpointMatches = enabled.Where(runtime => EndpointMatches(runtime.Profile, activity.Endpoint)).ToArray();
        if (endpointMatches.Length == 1) return endpointMatches[0];

        // AI clients usually report their model API base (/v1), while the
        // saved monitor points at a balance endpoint (/v1/usage). The origin
        // is a safe fallback only when it identifies exactly one local profile.
        var originMatches = enabled.Where(runtime => EndpointOriginMatches(runtime.Profile, activity.Endpoint)).ToArray();
        return originMatches.Length == 1 ? originMatches[0] : null;
    }

    private static bool EndpointMatches(MonitorProfile profile, string endpoint)
    {
        var incoming = NormalizeAccountEndpoint(endpoint);
        if (incoming.Length == 0) return false;
        var profileEndpoint = NormalizeAccountEndpoint(profile.Endpoint);
        var siteEndpoint = NormalizeAccountEndpoint(profile.SiteUrl);
        return string.Equals(incoming, profileEndpoint, StringComparison.OrdinalIgnoreCase)
            || string.Equals(incoming, siteEndpoint, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndpointOriginMatches(MonitorProfile profile, string endpoint)
    {
        var incomingOrigin = AccountEndpointOrigin(endpoint);
        if (incomingOrigin.Length == 0) return false;
        return string.Equals(incomingOrigin, AccountEndpointOrigin(profile.Endpoint), StringComparison.OrdinalIgnoreCase)
            || string.Equals(incomingOrigin, AccountEndpointOrigin(profile.SiteUrl), StringComparison.OrdinalIgnoreCase);
    }

    private static string AccountEndpointOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string NormalizeAccountEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return "";
        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/v1/usage", "/api/usage/token" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length].TrimEnd('/');
                break;
            }
        }
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + path;
    }

    private static string FingerprintToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "";
        var normalized = token.Trim();
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) normalized = normalized[7..].Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string DisplayCurrency(string? currency) => string.IsNullOrWhiteSpace(currency) ? "" : currency.Trim().ToUpperInvariant();

    private static string FormatAccountBalance(double amount, string? currency)
    {
        var unit = DisplayCurrency(currency);
        return unit.Length == 0 ? $"{amount:0.00}" : $"{amount:0.00} {unit}";
    }

    private Task StartCodexTaskAsync(CodexTaskActivity activity)
    {
        PruneRecentTaskStops();
        if (WasRecentlyStopped(activity)) return Task.CompletedTask;
        _retiredTaskKeys.Remove(activity.Key);

        // A resumed Codex turn keeps its session id but may receive a new
        // turn id. Replace the abandoned turn from that same session so a
        // missing Stop event cannot make the next task look like a second job.
        ReplaceActiveTaskForSession(activity);
        if (!_activeCodexTurns.Add(activity.Key)) return Task.CompletedTask;
        _activeTaskSources[activity.Key] = TaskSourceLabel(activity.Provider);
        _activeTaskProfiles[activity.Key] = FindMonitorProfile(activity.Provider)?.Id ?? "";
        ResetInactiveTimer();
        _codexHideTimer.Stop();
        if (!IsVisible)
        {
            _codexShownPet = true;
            Show();
        }

        if (_activeCodexTurns.Count == 1)
        {
            _codexStartBalances.Clear();
        }
        var taskProfile = FindMonitorProfile(activity.Provider);
        var taskProfileId = taskProfile?.Id ?? SelectedMonitor?.Profile.Id ?? "";
        if (!_codexStartBalances.ContainsKey(taskProfileId))
            _codexStartBalances[taskProfileId] = taskProfileId.Length > 0 && _monitorStates.TryGetValue(taskProfileId, out var taskRuntime) ? taskRuntime.LastBalance : _lastBalance;

        if (!_activeCodexTurns.Contains(activity.Key)) return Task.CompletedTask;
        SetStatus("AI 工作中");
        SetVisualState(PetVisualState.CodexWorking);
        ShowBubble(
            $"{CurrentTaskSourceLabel()} 工作中",
            _activeCodexTurns.Count == 1 ? "正在处理" : $"{_activeCodexTurns.Count} 个任务",
            "任务完成或停止后会自动切换状态");
        return Task.CompletedTask;
    }

    private Task CompleteCodexTaskAsync(CodexTaskActivity activity)
    {
        PruneRecentTaskStops();
        if (!RemoveActiveCodexTurn(activity, out var completedSource, out var completedProfileId))
        {
            // Keep a very short marker for out-of-order start/stop messages.
            // It expires quickly so a real later task is unaffected.
            RememberUnmatchedStop(activity);
            return Task.CompletedTask;
        }
        ResetInactiveTimer();
        if (_activeCodexTurns.Count > 0)
        {
            SetVisualState(PetVisualState.CodexWorking);
            ShowBubble($"{CurrentTaskSourceLabel()} 工作中", $"{_activeCodexTurns.Count} 个任务", "仍有任务正在处理");
            return Task.CompletedTask;
        }

        var selected = SelectedMonitor;
        var completedProfile = _monitorStates.TryGetValue(completedProfileId, out var completedRuntime) ? completedRuntime : selected;
        SyncSelectedMonitorState();
        var currency = completedProfile?.Profile.Currency ?? selected?.Profile.Currency ?? _settings.Currency;
        var spentByCurrency = _codexStartBalances
            .Select(pair => _monitorStates.TryGetValue(pair.Key, out var runtime) && pair.Value.HasValue && runtime.LastBalance.HasValue && string.Equals(runtime.Profile.Currency, currency, StringComparison.OrdinalIgnoreCase)
                ? Math.Max(0, pair.Value.Value - runtime.LastBalance.Value)
                : 0)
            .Sum();
        var spent = spentByCurrency;
        SetVisualState(PetVisualState.CodexDone, CodexDoneDurationMs);
        ShowBubble($"{completedSource} 已停止", spent > 0 ? $"-{spent:0.00} {currency}" : "任务结束", spent > 0 ? $"当前余额 {_lastBalance:0.00} {currency}" : "已完成或手动停止");
        ShowSystemNotification($"{completedSource} 任务已停止", spent > 0 ? $"本次消耗 {spent:0.00} {currency}" : "任务已完成或手动停止", Forms.ToolTipIcon.Info);
        _codexStartBalances.Clear();
        _ = RefreshAfterCodexCompletionAsync();
        if (_codexShownPet)
        {
            _codexHideTimer.Stop();
            _codexHideTimer.Start();
        }
        return Task.CompletedTask;
    }

    private string CurrentTaskSourceLabel()
    {
        var sources = _activeTaskSources
            .Where(pair => _activeCodexTurns.Contains(pair.Key))
            .Select(pair => pair.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return sources.Length == 1 ? sources[0] : "AI 任务";
    }

    private static string TaskSourceLabel(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return "Codex";
        var value = provider.Trim();
        return value.Length <= 32 ? value : value[..32];
    }

    private async Task RefreshAfterCodexCompletionAsync()
    {
        await Task.Delay(CodexDoneDurationMs);
        if (_closing || _activeCodexTurns.Count > 0) return;
        await RefreshAsync(false, false, true);
    }

    private bool RemoveActiveCodexTurn(CodexTaskActivity activity, out string completedSource, out string completedProfileId)
    {
        completedSource = TaskSourceLabel(activity.Provider);
        completedProfileId = "";

        if (!string.IsNullOrWhiteSpace(activity.TurnId))
        {
            if (RemoveTaskKey(activity.Key, ref completedSource, out completedProfileId)) return true;
            // A previous start from this session may have been replaced by a
            // resumed turn. Its delayed Stop must not end the newer turn.
            if (_retiredTaskKeys.ContainsKey(activity.Key)) return false;

            // Some hosts rotate turn_id between start and Stop. If this key
            // was not retired, match the currently active turn in the same
            // session rather than leaving the task stuck forever.
            var sessionPrefix = activity.SessionId + ":";
            var matchingKey = _activeCodexTurns.FirstOrDefault(key =>
                key.StartsWith(sessionPrefix, StringComparison.Ordinal));
            if (matchingKey is not null) return RemoveTaskKey(matchingKey, ref completedSource, out completedProfileId);
            return false;
        }

        // Hosts may omit turn_id on Stop. Only then use the session or sole-task
        // fallback; a stale event with an old, non-empty turn_id must never end
        // a newer resumed turn from the same session.
        if (string.IsNullOrWhiteSpace(activity.TurnId))
        {
            var sessionPrefix = activity.SessionId + ":";
            var matchingKey = _activeCodexTurns.FirstOrDefault(key =>
                key.StartsWith(sessionPrefix, StringComparison.Ordinal));
            if (matchingKey is not null) return RemoveTaskKey(matchingKey, ref completedSource, out completedProfileId);

            if (_activeCodexTurns.Count == 1)
            {
                var soleKey = _activeCodexTurns.First();
                return RemoveTaskKey(soleKey, ref completedSource, out completedProfileId);
            }

            // A Stop without identity still represents one completed turn. If
            // several tasks remain, consume one rather than staying stuck.
            if (_activeCodexTurns.Count > 0)
            {
                var fallbackKey = _activeCodexTurns.First();
                return RemoveTaskKey(fallbackKey, ref completedSource, out completedProfileId);
            }
        }
        return false;
    }

    private bool RemoveTaskKey(string key, ref string completedSource, out string profileId)
    {
        profileId = "";
        if (!_activeCodexTurns.Remove(key)) return false;
        if (_activeTaskSources.Remove(key, out var source)) completedSource = source;
        if (_activeTaskProfiles.TryGetValue(key, out var knownProfileId)) profileId = knownProfileId ?? "";
        _activeTaskProfiles.Remove(key);
        return true;
    }

    private void ReplaceActiveTaskForSession(CodexTaskActivity activity)
    {
        var sessionPrefix = activity.SessionId + ":";
        var replacedKeys = _activeCodexTurns
            .Where(key => key.StartsWith(sessionPrefix, StringComparison.Ordinal) && !string.Equals(key, activity.Key, StringComparison.Ordinal))
            .ToArray();
        foreach (var key in replacedKeys)
        {
            _retiredTaskKeys[key] = DateTimeOffset.UtcNow;
            var ignoredSource = "Codex";
            RemoveTaskKey(key, ref ignoredSource, out _);
        }
    }

    private void PruneRecentTaskStops()
    {
        var cutoff = DateTimeOffset.UtcNow - TaskStopReorderWindow;
        foreach (var key in _recentTaskStops
                     .Where(pair => pair.Value < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _recentTaskStops.Remove(key);
        }

        var retiredCutoff = DateTimeOffset.UtcNow - RetiredTaskWindow;
        foreach (var key in _retiredTaskKeys
                     .Where(pair => pair.Value < retiredCutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _retiredTaskKeys.Remove(key);
        }
    }

    private void RememberUnmatchedStop(CodexTaskActivity activity)
    {
        var stoppedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(activity.TurnId)) _recentTaskStops[activity.Key] = stoppedAt;
    }

    private bool WasRecentlyStopped(CodexTaskActivity activity)
    {
        var now = DateTimeOffset.UtcNow;
        var matched = false;
        if (!string.IsNullOrWhiteSpace(activity.TurnId)
            && _recentTaskStops.TryGetValue(activity.Key, out var exactStop))
        {
            _recentTaskStops.Remove(activity.Key);
            matched = now - exactStop <= TaskStopReorderWindow;
        }

        return matched;
    }

    private MonitorProfile? FindMonitorProfile(string? provider)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var value = provider.Trim();
            var exact = _settings.Monitors.FirstOrDefault(profile =>
                string.Equals(profile.Id, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Name, value, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }
        return SelectedMonitor?.Profile;
    }

    private void HideAfterCodexCompletion()
    {
        _codexHideTimer.Stop();
        if (!_codexShownPet && !_temporarilyShownForUpdate) return;
        HideBubble();
        Hide();
        _codexShownPet = false;
        _temporarilyShownForUpdate = false;
    }

    private void ShowSystemNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (!_settings.SystemNotifications || _trayIcon is null) return;
        try { _trayIcon.ShowBalloonTip(5000, title, message, icon); } catch (InvalidOperationException) { }
    }
}
