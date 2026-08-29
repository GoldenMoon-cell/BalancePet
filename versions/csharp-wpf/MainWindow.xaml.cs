using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using BalancePet.Wpf.Models;
using BalancePet.Wpf.Services;

namespace BalancePet.Wpf;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly DpapiTokenStore _tokenStore = new();
    private readonly UsageLedgerStore _usageStore = new();
    private readonly BalanceCacheStore _balanceCache = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _floatTimer;
    private readonly DispatcherTimer _codexTimer;
    private readonly DispatcherTimer _stateTimer;
    private readonly DispatcherTimer _inactiveTimer;
    private readonly DispatcherTimer _bubbleAnimationTimer;
    private readonly DispatcherTimer _bubbleContentTimer;
    private readonly DispatcherTimer _amountAnimationTimer;
    private readonly DispatcherTimer _codexHideTimer;
    private readonly System.Windows.Media.MediaPlayer _pressSound = new();
    private readonly System.Windows.Media.MediaPlayer _releaseSound = new();
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
    private System.Drawing.Icon? _trayImage;
    private PetSettings _settings = new();
    private bool _closing;
    private System.Windows.Point _dragStart;
    private int _windowStartX;
    private int _windowStartY;
    private bool _dragging;
    private bool _dragMoved;
    private bool _codexWasRunning;
    private bool _codexTaskSeen;
    private bool _codexShownPet;
    private DateTimeOffset _lastErrorNotification = DateTimeOffset.MinValue;
    private double? _codexStartBalance;
    private double? _lastBalance;
    private double _todayUsage;
    private bool _hasBalance;
    private bool _refreshing;
    private DateTimeOffset _lastRefreshAttempt = DateTimeOffset.MinValue;
    private bool _codexSyncing;
    private int _bubbleCycle;
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
    private PetVisualState _visualState = PetVisualState.Idle;
    private TranslateTransform _petTranslate = new();
    private ScaleTransform _petScale = new(1, 1);
    private RotateTransform _petRotate = new();
    private IntPtr WindowHandle => new WindowInteropHelper(this).Handle;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToNearest = 0x00000002;

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

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

    public MainWindow()
    {
        InitializeComponent();
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(false);
        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _bubbleTimer.Tick += (_, _) => HideBubble();
        _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _floatTimer.Tick += (_, _) => AnimatePet();
        _codexTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _codexTimer.Tick += async (_, _) => await SyncCodexAsync();
        _stateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _stateTimer.Tick += (_, _) => RestoreSteadyVisualState();
        _inactiveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _inactiveTimer.Tick += (_, _) => SetVisualState(PetVisualState.Inactive);
        _bubbleAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _bubbleAnimationTimer.Tick += (_, _) => AnimateBubble();
        _bubbleContentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _bubbleContentTimer.Tick += (_, _) => AnimateBubbleContent();
        _amountAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _amountAnimationTimer.Tick += (_, _) => AnimateAmount();
        _codexHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _codexHideTimer.Tick += (_, _) => HideAfterCodexCompletion();
        Loaded += async (_, _) => { LoadSettingsAndPosition(); await RefreshAsync(true); };
        Closing += (_, e) =>
        {
            if (!_closing) { e.Cancel = true; Hide(); return; }
            SavePosition(); DisposeTray(); _httpClient.Dispose();
        };
    }

    private void LoadSettingsAndPosition()
    {
        _settings = _settingsStore.Load();
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
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(30, _settings.RefreshSeconds));
        _refreshTimer.Start();
        _floatTimer.Start();
        ResetInactiveTimer();
        ConfigureSounds();
        _codexTimer.Stop();
        _codexWasRunning = _settings.FollowCodex && CodexIsRunning();
        _codexShownPet = _codexWasRunning;
        _codexTaskSeen = false;
        _codexStartBalance = null;
        if (_settings.FollowCodex) _codexTimer.Start();
        SetupTray();
        if (_settings.FollowCodex && !_codexWasRunning)
        {
            // In follow mode the tray remains available while the pet waits
            // for Codex to start; the process monitor will show it on launch.
            Hide();
        }
        else if (!_settings.FollowCodex && !IsVisible)
        {
            Show();
        }
    }

    private async Task RefreshAsync(bool manual)
    {
        if (_closing) return;
        if (string.IsNullOrWhiteSpace(_settings.Endpoint)) { SetStatus("请先完成接口设置"); ShowBubble("还没配置", "--", "点击齿轮填写接口"); return; }
        if (_refreshing) return;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRefreshAttempt < TimeSpan.FromSeconds(30))
        {
            if (manual)
            {
                var remaining = Math.Max(1, Math.Ceiling((TimeSpan.FromSeconds(30) - (now - _lastRefreshAttempt)).TotalSeconds));
                ShowBubble("请稍候", $"{remaining:0} 秒", "两次请求至少间隔 30 秒");
            }
            return;
        }
        _lastRefreshAttempt = now;
        _refreshing = true;
        try
        {
            ResetInactiveTimer();
            SetStatus("正在查询");
            SetVisualState(PetVisualState.Loading);
            if (manual) ShowBubble("正在刷新", "--", "正在联系中转站");
            var token = _tokenStore.Unprotect(_settings.TokenBlob);
            var snapshot = await new JsonBalanceProvider(_httpClient).FetchAsync(_settings, token);
            SetStatus(snapshot.Amount <= _settings.LowThreshold ? "余额偏低" : "查询成功");
            var hadBalance = _hasBalance;
            var wasLow = hadBalance && _lastBalance <= _settings.LowThreshold;
            var observation = _usageStore.Record(snapshot.Amount, snapshot.Currency, snapshot.UpdatedAt);
            _balanceCache.Save(snapshot);
            AnimateAmountTo(snapshot.Amount, snapshot.Currency);
            _lastBalance = snapshot.Amount;
            _hasBalance = true;
            _todayUsage = observation.TodayUsage;
            if (manual) PlaySound(_releaseSound);
            if (observation.Spent > 0.000001)
            {
                SetVisualState(PetVisualState.Clicked, 1200);
                ShowBubble("本次消耗", $"-{observation.Spent:0.00} {observation.Currency}", $"当前 {snapshot.Amount:0.00} {observation.Currency} · 今日已用 {observation.TodayUsage:0.00}");
            }
            else
            {
                SetVisualState(snapshot.Amount <= _settings.LowThreshold ? PetVisualState.Low : PetVisualState.Success);
                if (snapshot.Amount <= _settings.LowThreshold && (!wasLow || !hadBalance))
                    ShowSystemNotification("余额偏低", $"当前余额 {snapshot.Amount:0.00} {snapshot.Currency}", Forms.ToolTipIcon.Warning);
                if (manual || !hadBalance) ShowBubble("账户余额", $"{snapshot.Amount:0.00} {snapshot.Currency}", $"更新于 {snapshot.UpdatedAt:HH:mm:ss} · 今日已用 {observation.TodayUsage:0.00}");
            }
        }
        catch (Exception error)
        {
            SetStatus("刷新失败");
            SetVisualState(PetVisualState.Error);
            var detail = error.Message.Length > 90 ? error.Message[..90] + "…" : error.Message;
            if (_balanceCache.TryLoad(out var cached))
            {
                _lastBalance = cached.Amount;
                _hasBalance = true;
                AnimateAmountTo(cached.Amount, cached.Currency);
                ShowBubble("上次余额", $"{cached.Amount:0.00} {cached.Currency}", $"网络波动，暂用缓存 · {detail}");
            }
            else ShowBubble("刷新失败", "--", detail);
            if (DateTimeOffset.Now - _lastErrorNotification > TimeSpan.FromMinutes(10))
            {
                ShowSystemNotification("余额刷新失败", detail, Forms.ToolTipIcon.Error);
                _lastErrorNotification = DateTimeOffset.Now;
            }
        }
        finally { _refreshing = false; }
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
        var style = _settings.PetStyle.Equals("chatgpt", StringComparison.OrdinalIgnoreCase) ? "chatgpt" : "deepseek";
        var stateName = state switch
        {
            PetVisualState.CodexWorking => "codex-working",
            PetVisualState.CodexDone => "codex-done",
            _ => state.ToString().ToLowerInvariant()
        };
        var baseName = style == "chatgpt" ? "chatgpt-dragon.png" : "pet.png";
        var statePath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "pets", style, $"{stateName}.png");
        var basePath = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", baseName);
        var selectedPath = File.Exists(statePath) ? statePath : basePath;
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
        if (!_hasBalance) SetVisualState(PetVisualState.Idle);
        else if (_lastBalance <= _settings.LowThreshold) SetVisualState(PetVisualState.Low);
        else SetVisualState(PetVisualState.Success);
    }

    private void ResetInactiveTimer()
    {
        _inactiveTimer.Stop();
        _inactiveTimer.Start();
        if (_visualState == PetVisualState.Inactive) RestoreSteadyVisualState();
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
        ResetInactiveTimer();
        SetVisualState(PetVisualState.Clicked);
        StartSquashAnimation(1);
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
        if (!GetWindowRect(WindowHandle, out var rect)) return;
        _dragStart = new System.Windows.Point(cursor.X, cursor.Y); _windowStartX = rect.Left; _windowStartY = rect.Top; _dragging = true; _dragMoved = false; PetSurface.CaptureMouse();
        PlaySound(_pressSound);
    }
    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_settings.InteractionMode == "locked")
        {
            if (PetSurface.IsMouseCaptured) PetSurface.ReleaseMouseCapture();
            _lockedPressed = false; PlaySound(_releaseSound);
            KickReleaseBounce();
            ShowBubble(_lockedKind == "hair" ? "呆毛被提起" : _lockedKind == "mouth" ? "嘴角被拽住" : "被戳到了", _lockedKind == "body" ? "在呢" : "哎呀", _lockedKind == "body" ? "点击角色刷新余额" : "轻一点嘛");
            if (_lockedKind == "body") _ = RefreshAsync(true);
            _interactionTargetX = 0; _interactionTargetY = 0;
            if (_lockedKind != "body") RestoreSteadyVisualState();
            return;
        }
        if (!_dragging) return;
        _dragging = false; if (PetSurface.IsMouseCaptured) PetSurface.ReleaseMouseCapture();
        SnapToEdge();
        KickReleaseBounce();
        PlaySound(_releaseSound); if (!_dragMoved) _ = RefreshAsync(true); else RestoreSteadyVisualState();
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
        else if (_lockedPressed && e.LeftButton == MouseButtonState.Pressed)
        {
            var point = PointToScreen(e.GetPosition(this)); var dx = Math.Clamp(point.X - _lockedStart.X, -45, 45); var dy = Math.Clamp(point.Y - _lockedStart.Y, -55, 35);
            _interactionTargetX = _lockedKind == "mouth" ? dx * .18 : _lockedKind == "hair" ? dx * .08 : 0;
            _interactionTargetY = _lockedKind == "hair" ? dy * .16 : 0;
        }
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void OnPetContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        BubbleMenuItem.Header = BubbleGroup.Visibility == Visibility.Visible ? "隐藏气泡" : "显示气泡";
        InteractionMenuItem.Header = string.Equals(_settings.InteractionMode, "locked", StringComparison.OrdinalIgnoreCase)
            ? "切换为自由拖动"
            : "切换为锁定互动";
    }

    private async void OnContextRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void OnContextBubbleClick(object sender, RoutedEventArgs e)
    {
        if (BubbleGroup.Visibility == Visibility.Visible)
        {
            HideBubble();
            return;
        }

        if (_hasBalance)
            ShowBubble("账户余额", $"{_lastBalance:0.00} {_settings.Currency}", $"今日已用 {_todayUsage:0.00} {_settings.Currency}");
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

    private void OnContextSettingsClick(object sender, RoutedEventArgs e) => OnSettingsClick(this, new RoutedEventArgs());

    private void OnContextUsageClick(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(OpenUsageWindow));

    private void OnContextHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnContextExitClick(object sender, RoutedEventArgs e)
    {
        _closing = true;
        System.Windows.Application.Current.Shutdown();
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settingsStore, _tokenStore, _settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            LoadSettingsAndPosition();
            ConfigureSounds();
            await RefreshAsync(true);
        }
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
        // Match Electron: the window snaps once its center enters an outer
        // quarter of the monitor, which is much easier to trigger than a
        // fixed pixel-distance threshold on transparent windows.
        var centerX = rect.Left + width / 2.0;
        var centerY = rect.Top + height / 2.0;
        if (centerX < info.Work.Left + (info.Work.Right - info.Work.Left) / 4.0)
            x = info.Work.Left;
        else if (centerX > info.Work.Left + (info.Work.Right - info.Work.Left) * .75)
            x = info.Work.Right - width;
        if (centerY < info.Work.Top + (info.Work.Bottom - info.Work.Top) / 4.0)
            y = info.Work.Top;
        else if (centerY > info.Work.Top + (info.Work.Bottom - info.Work.Top) * .75)
            y = info.Work.Bottom - height;
        x = Math.Clamp(x, info.Work.Left, info.Work.Right - width);
        y = Math.Clamp(y, info.Work.Top, info.Work.Bottom - height);
        _settings.Flipped = x == info.Work.Left;
        ApplyFlipVisuals();
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
            ShowCheckMargin = false,
            ShowItemToolTips = false,
            RenderMode = Forms.ToolStripRenderMode.System,
            Margin = new System.Windows.Forms.Padding(0),
            Padding = new System.Windows.Forms.Padding(0)
        };
        menu.Items.Add("显示桌宠", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync(true));
        menu.Items.Add("配置接口", null, (_, _) => OnSettingsClick(this, new RoutedEventArgs()));
        menu.Items.Add("用量统计", null, (_, _) => Dispatcher.BeginInvoke(new Action(OpenUsageWindow)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => { _closing = true; System.Windows.Application.Current.Shutdown(); });
        _trayMenu = menu;
        // Assign the icon before making the shell icon visible. Some Explorer
        // versions do not repaint a NotifyIcon that started with Icon == null.
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "小余额",
            Icon = _trayImage,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => { Show(); Activate(); };
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
        _trayImage?.Dispose();
        _trayImage = null;
    }

    private void OpenUsageWindow()
    {
        var dialog = new UsageWindow(_usageStore) { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowBubble(string label, string amount, string hint)
    {
        if (!_settings.Bubble) return;
        _bubbleCycle = 0;
        _pendingBubbleLabel = label;
        _pendingBubbleAmount = amount;
        _pendingBubbleHint = hint;
        if (BubbleGroup.Visibility != Visibility.Visible)
        {
            BubbleLabel.Text = label;
            BubbleAmount.Text = amount;
            BubbleHint.Text = hint;
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
    private void OnBubbleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ResetInactiveTimer();
        var lines = new[]
        {
            ("状态良好", "余额充足", "今天也可以安心工作"),
            ("我看着呢", "不会漏掉", "余额变化会及时告诉你"),
            ("省着点花", "细水长流", "余额偏低时我会提醒你"),
            ("工作时间", "陪你写完", "完成后会显示本次消耗")
        };
        var line = lines[_bubbleCycle++ % lines.Length];
        _pendingBubbleLabel = line.Item1;
        _pendingBubbleAmount = line.Item2;
        _pendingBubbleHint = line.Item3;
        BubbleContent.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(80))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        });
        _bubbleContentTimer.Stop();
        _bubbleContentTimer.Start();
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    private void AnimateBubble()
    {
        _bubbleAnimationProgress = Math.Min(1, _bubbleAnimationProgress + 0.016 / 0.32);
        var eased = ElectronCubicBezier(_bubbleAnimationProgress, .34, 1.56, .64, 1);
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
        BubbleLabel.Text = _pendingBubbleLabel;
        BubbleAmount.Text = _pendingBubbleAmount;
        BubbleHint.Text = _pendingBubbleHint;
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
            BubbleAmount.Text = $"{_displayBalance:0.00} {_amountCurrency}";
        if (_amountAnimationProgress >= 1) _amountAnimationTimer.Stop();
    }
    private void AnimatePet()
    {
        EnsurePetTransforms();
        var now = Environment.TickCount64;
        if (_lastAnimationTick == 0) _lastAnimationTick = now;
        var elapsed = Math.Clamp((now - _lastAnimationTick) / 1000.0, 0.008, 0.05);
        _lastAnimationTick = now;
        var held = _dragging || _lockedPressed;
        UpdateSquashAnimation(elapsed);
        _interactionX += (_interactionTargetX - _interactionX) * (1 - Math.Exp(-elapsed * 16));
        _interactionY += (_interactionTargetY - _interactionY) * (1 - Math.Exp(-elapsed * 16));
        var squashX = 1 + _squashProgress * .05;
        var squashY = 1 - _squashProgress * .12;
        var tilt = 0d;
        if (_lockedKind == "hair") tilt += Math.Clamp(_interactionX * .12, -5.5, 5.5);
        else if (_lockedKind == "mouth") tilt += Math.Clamp(_interactionX * .045, -2.5, 2.5);
        _petTranslate.X = _interactionX;
        _petTranslate.Y = _interactionY;
        _petScale.ScaleX = squashX * (_settings.Flipped ? -1 : 1);
        _petScale.ScaleY = squashY;
        _petRotate.Angle = tilt;
    }

    private void StartSquashAnimation(double target)
    {
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
        var eased = ElectronCubicBezier(progress, .34, 1.56, .64, 1.0);
        _squashProgress = _squashFrom + (_squashTo - _squashFrom) * eased;
        if (progress >= 1)
        {
            _squashProgress = _squashTo;
            _squashAnimating = false;
        }
    }

    // Evaluates the same overshooting cubic-bezier as Electron's CSS transition.
    private static double ElectronCubicBezier(double x, double x1, double y1, double x2, double y2)
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

    private static bool CodexIsRunning() => Process.GetProcessesByName("codex").Length > 0 || Process.GetProcessesByName("chatgpt").Length > 0;

    private async Task SyncCodexAsync()
    {
        if (_codexSyncing) return;
        _codexSyncing = true;
        try
        {
        var running = CodexIsRunning();
        if (running && !_codexWasRunning)
        {
            _codexShownPet = true;
            Show();
            _codexTaskSeen = true; SetStatus("正在查询"); SetVisualState(PetVisualState.CodexWorking); ResetInactiveTimer(); ShowBubble("Codex 工作中", "--", "正在处理任务");
            await RefreshAsync(false);
            _codexStartBalance = _lastBalance;
            SetVisualState(PetVisualState.CodexWorking);
            ShowBubble("Codex 工作中", "--", "正在处理任务");
        }
        else if (!running && _codexWasRunning && _codexTaskSeen)
        {
            await RefreshAsync(false);
            var spent = _codexStartBalance.HasValue && _lastBalance.HasValue ? Math.Max(0, _codexStartBalance.Value - _lastBalance.Value) : 0;
            SetVisualState(PetVisualState.CodexDone, 6200);
            ShowBubble("Codex 完成", spent > 0 ? $"-{spent:0.00} {_settings.Currency}" : "任务结束", spent > 0 ? $"当前余额 {_lastBalance:0.00} {_settings.Currency}" : "余额变化将在刷新后显示");
            ShowSystemNotification("Codex 任务完成", spent > 0 ? $"本次消耗 {spent:0.00} {_settings.Currency}" : "任务已结束", Forms.ToolTipIcon.Info);
            _codexTaskSeen = false; _codexStartBalance = null;
            if (_codexShownPet)
            {
                _codexHideTimer.Stop();
                _codexHideTimer.Start();
            }
        }
        _codexWasRunning = running;
        }
        finally { _codexSyncing = false; }
    }

    private void HideAfterCodexCompletion()
    {
        _codexHideTimer.Stop();
        if (!_codexShownPet) return;
        HideBubble();
        Hide();
        _codexShownPet = false;
    }

    private void ShowSystemNotification(string title, string message, Forms.ToolTipIcon icon)
    {
        if (!_settings.SystemNotifications || _trayIcon is null) return;
        try { _trayIcon.ShowBalloonTip(5000, title, message, icon); } catch (InvalidOperationException) { }
    }
}
