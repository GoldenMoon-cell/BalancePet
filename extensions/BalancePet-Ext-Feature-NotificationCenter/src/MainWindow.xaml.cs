using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BalancePet.NotificationCenter;

public partial class MainWindow : Window
{
    private readonly NotificationEventStore _store;
    private readonly ObservableCollection<NotificationRow> _rows = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly BubbleWindow _infoWindow = new();
    private string _filter = "all";
    private string _lastFingerprint = "";
    private string _coreVersion = "";

    public MainWindow()
    {
        _store = new NotificationEventStore(ReadDataDirectory(Environment.GetCommandLineArgs()));
        InitializeComponent();
        ApplySystemTheme();
        EventsList.ItemsSource = _rows;
        _refreshTimer.Tick += (_, _) => Refresh(false);
        Refresh();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
        Closed += (_, _) => _infoWindow.Close();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Hide();

    private void FilterChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _filter = tag;
            if (!IsLoaded) return;
            Refresh(true);
        }
    }

    private void Refresh(bool force = true)
    {
        var all = _store.ReadAll();
        var liveState = NotificationLiveState.Read(_store.DirectoryPath);
        if (!string.IsNullOrWhiteSpace(liveState?.CoreVersion)) _coreVersion = liveState.CoreVersion;
        if (string.IsNullOrWhiteSpace(_coreVersion)) _coreVersion = ReadCoreVersion();
        _infoWindow.UpdateItems(CreateAroundPetItems(all, _coreVersion, liveState));
        var fingerprint = all.Count == 0 ? "empty" : $"{all.Count}:{all[0].EventId}:{_filter}";
        if (!force && string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)) return;
        _lastFingerprint = fingerprint;
        var filtered = _filter == "all"
            ? all
            : all.Where(value => string.Equals(value.Category, _filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        _rows.Clear();
        foreach (var row in filtered) _rows.Add(new NotificationRow(row));
        SummaryText.Text = _filter == "all"
            ? $"最近 {all.Count:N0} 条消息"
            : $"{CategoryText(_filter)} · {filtered.Count:N0} 条消息";
        EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EventsList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = "本地保存 · 自动随桌宠更新";
    }

    private static IReadOnlyList<NotificationBubble> CreateAroundPetItems(
        IReadOnlyList<NotificationEvent> all,
        string coreVersion,
        NotificationLiveState? liveState)
    {
        var items = new List<NotificationBubble>();

        var balance = liveState is not null ? CreateLiveBalanceBubble(liveState) : CreateBalanceBubble(all);
        if (balance is not null) items.Add(balance);

        if (liveState?.LoginKnown == true)
        {
            var mode = string.IsNullOrWhiteSpace(liveState.LoginMode) ? "未登录" : liveState.LoginMode;
            var detail = string.IsNullOrWhiteSpace(liveState.LoginDetail) ? "等待账户切换" : liveState.LoginDetail;
            items.Add(new NotificationBubble($"当前登录方式 · {mode}", detail, "account"));
        }
        else
        {
            var login = all.FirstOrDefault(IsSelectedLoginStatus);
            if (login is not null)
            {
                var official = login.Title.Contains("官方", StringComparison.OrdinalIgnoreCase)
                    || login.Detail.StartsWith("官方 API", StringComparison.OrdinalIgnoreCase);
                var detail = string.Join(" · ", new[] { login.Title, login.Amount }
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "--", StringComparison.Ordinal)));
                items.Add(new NotificationBubble($"当前登录方式 · {(official ? "官方登录" : "CC Switch")}", detail, "account"));
            }
        }

        if (liveState?.TaskKnown == true)
        {
            var provider = string.IsNullOrWhiteSpace(liveState.TaskProvider) ? "AI 任务" : liveState.TaskProvider;
            var detail = liveState.TaskActive
                ? liveState.TaskCount > 1 ? $"{liveState.TaskCount} 个任务" : "正在处理"
                : "当前没有正在处理的任务";
            items.Add(new NotificationBubble($"{provider} {(liveState.TaskActive ? "工作中" : "已停止")}", detail, "task"));
        }
        else
        {
            var task = all.FirstOrDefault(item =>
                item.Title.Contains("工作中", StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains("已停止", StringComparison.OrdinalIgnoreCase));
            if (task is not null) items.Add(ToBubble(task, "task"));
        }

        if (!string.IsNullOrWhiteSpace(coreVersion))
            items.Add(new NotificationBubble($"当前版本 · {coreVersion}", "BalancePet", "system"));
        return items;
    }

    private static NotificationBubble? CreateBalanceBubble(IReadOnlyList<NotificationEvent> all)
    {
        var current = all.FirstOrDefault(item => TitleIs(item, "账户余额") && HasAmount(item));
        var previous = all.FirstOrDefault(item => TitleIs(item, "上次余额") && HasAmount(item));
        var spent = all.FirstOrDefault(item => TitleIs(item, "本次消耗") && HasAmount(item));

        var balances = new[]
        {
            current is null ? "" : $"余额 {current.Amount.Trim()}",
            previous is null ? "" : $"上次 {previous.Amount.Trim()}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

        var primary = string.Join(" · ", balances);
        var detail = spent is null ? "" : $"本次消耗 {spent.Amount.Trim()}";
        if (string.IsNullOrWhiteSpace(primary))
        {
            primary = detail;
            detail = "";
        }

        return string.IsNullOrWhiteSpace(primary)
            ? null
            : new NotificationBubble(primary, detail, "balance");
    }

    private static NotificationBubble? CreateLiveBalanceBubble(NotificationLiveState state)
    {
        if (!state.HasBalance) return null;
        var currency = string.IsNullOrWhiteSpace(state.Currency) ? "USD" : state.Currency;
        var primary = $"余额 {state.Balance!.Value:0.00} {currency}";
        var detail = state.HasSpent
            ? $"本次消耗 {(state.Spent!.Value > 0 ? "-" : "")}{Math.Abs(state.Spent.Value):0.00} {(string.IsNullOrWhiteSpace(state.SpentCurrency) ? currency : state.SpentCurrency)}"
            : "等待下一次余额刷新";
        return new NotificationBubble(primary, detail, "balance");
    }

    private static NotificationBubble ToBubble(NotificationEvent item, string kind)
    {
        var primary = string.Join(" · ", new[] { item.Title, item.Amount }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value.Trim(), "--", StringComparison.Ordinal)));
        return new NotificationBubble(primary, item.Detail, kind);
    }

    private static bool TitleIs(NotificationEvent item, params string[] titles)
        => titles.Any(title => string.Equals(item.Title, title, StringComparison.OrdinalIgnoreCase));

    private static bool HasAmount(NotificationEvent item)
        => !string.IsNullOrWhiteSpace(item.Amount)
            && !string.Equals(item.Amount.Trim(), "--", StringComparison.Ordinal);

    private static bool IsSelectedLoginStatus(NotificationEvent item)
    {
        if (!string.Equals(item.Category, "account", StringComparison.OrdinalIgnoreCase)) return false;
        if (item.Title.Contains("官方账户已登录", StringComparison.OrdinalIgnoreCase)
            || item.Title.Contains("官方 API 已登录", StringComparison.OrdinalIgnoreCase)) return true;
        return item.Title.Contains("API 已登录", StringComparison.OrdinalIgnoreCase)
            || item.Detail.StartsWith("官方 API", StringComparison.OrdinalIgnoreCase)
            || item.Detail.StartsWith("已匹配本地账户", StringComparison.OrdinalIgnoreCase)
            || item.Detail.StartsWith("CC Switch", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadCoreVersion()
    {
        try
        {
            var handle = FindWindow(null, "BalancePet");
            if (handle == IntPtr.Zero) return "";
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0) return "";
            using var process = Process.GetProcessById((int)processId);
            var executable = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable)) return "";
            var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "";
            return version.Split('+')[0].Trim();
        }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
    }

    private static string ReadDataDirectory(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--data-dir", StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return "";
    }

    private static string CategoryText(string category) => category switch
    {
        "balance" => "余额",
        "refresh" => "刷新",
        "task" => "任务",
        "account" => "账户",
        "system" => "系统",
        _ => "互动"
    };

    private void ApplySystemTheme()
    {
        var isLight = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            isLight = (key?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        }
        catch (Exception) { }

        var resources = Application.Current.Resources;
        if (isLight)
        {
            SetBrush(resources, "WindowBrush", Color.FromRgb(244, 246, 251));
            SetBrush(resources, "TitleBarBrush", Color.FromRgb(237, 241, 247));
            SetBrush(resources, "PanelBrush", Colors.White);
            SetBrush(resources, "CardBrush", Color.FromRgb(251, 252, 255));
            SetBrush(resources, "ControlHoverBrush", Color.FromRgb(232, 237, 246));
            SetBrush(resources, "BorderBrush", Color.FromRgb(215, 223, 238));
            SetBrush(resources, "TextBrush", Color.FromRgb(38, 50, 77));
            SetBrush(resources, "MutedBrush", Color.FromRgb(113, 128, 157));
            SetBrush(resources, "AccentBrush", Color.FromRgb(7, 140, 130));
            SetBrush(resources, "AccentSoftBrush", Color.FromRgb(221, 243, 240));
        }
        else
        {
            SetBrush(resources, "WindowBrush", Color.FromRgb(7, 17, 31));
            SetBrush(resources, "TitleBarBrush", Color.FromRgb(13, 26, 45));
            SetBrush(resources, "PanelBrush", Color.FromRgb(16, 31, 51));
            SetBrush(resources, "CardBrush", Color.FromRgb(20, 38, 61));
            SetBrush(resources, "ControlHoverBrush", Color.FromRgb(31, 53, 79));
            SetBrush(resources, "BorderBrush", Color.FromRgb(34, 57, 83));
            SetBrush(resources, "TextBrush", Color.FromRgb(241, 245, 252));
            SetBrush(resources, "MutedBrush", Color.FromRgb(147, 163, 186));
            SetBrush(resources, "AccentBrush", Color.FromRgb(45, 225, 194));
            SetBrush(resources, "AccentSoftBrush", Color.FromRgb(23, 65, 71));
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color) => resources[key] = new SolidColorBrush(color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private sealed class NotificationRow
    {
        private readonly NotificationEvent _event;
        public NotificationRow(NotificationEvent value) => _event = value;
        public string Title => string.IsNullOrWhiteSpace(_event.Title) ? "消息" : _event.Title;
        public string Amount => _event.Amount;
        public string Detail => _event.Detail;
        public string CategoryText => MainWindow.CategoryText(_event.Category);
        public string OccurredAtText => _event.OccurredAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
        public Brush CategoryBrush => _event.Category switch
        {
            "balance" => Brush("#078C82"),
            "refresh" => Brush("#344F91"),
            "task" => Brush("#8A5CC7"),
            "account" => Brush("#D38A1A"),
            "system" => Brush("#71809D"),
            _ => Brush("#C43B52")
        };
        public Visibility AmountVisibility => string.IsNullOrWhiteSpace(Amount) ? Visibility.Collapsed : Visibility.Visible;

        private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    }
}
