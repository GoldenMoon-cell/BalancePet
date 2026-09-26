using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Input;

namespace BalancePet.UsageAnalytics;

public partial class MainWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);
    private readonly UsageEventStore _store;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _fileRefreshTimer;
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _balanceWatcher;
    private FileSystemWatcher? _serverUsageWatcher;
    private bool _fileRefreshPending;
    private Button? _activeNav;
    private UsageReport? _lastReport;
    private BalanceUsageSnapshot _lastBalanceUsage = BalanceUsageSnapshot.Read("");
    private ServerUsageSummarySnapshot _lastServerUsage = ServerUsageSummarySnapshot.Read("");
    private IReadOnlyList<UsageEvent> _lastEvents = Array.Empty<UsageEvent>();
    private TrendRange _trendRange = TrendRange.Last7Days;
    private DateTimeOffset _nextRefreshAt = DateTimeOffset.Now.Add(AutoRefreshInterval);

    public MainWindow(string dataDirectory)
    {
        InitializeComponent();
        _store = new UsageEventStore(dataDirectory);
        _refreshTimer = new DispatcherTimer { Interval = AutoRefreshInterval };
        _refreshTimer.Tick += (_, _) => { RequestCoreRefresh(); Refresh(); };
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateAutoRefreshStatus();
        _fileRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _fileRefreshTimer.Tick += (_, _) =>
        {
            _fileRefreshTimer.Stop();
            _fileRefreshPending = false;
            Refresh();
        };
        Loaded += (_, _) =>
        {
            StartAutoRefresh();
            _clockTimer.Start();
            DrawTrendChart();
            UpdateAutoRefreshStatus();
            UpdateActiveNavigation();
        };
        Closed += (_, _) => StopAutoRefresh();
        Refresh();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInsideButton(e.OriginalSource as DependencyObject)) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnDashboardNavClick(object sender, RoutedEventArgs e)
    {
        ContentScrollViewer.Visibility = Visibility.Visible;
        HistoryScrollViewer.Visibility = Visibility.Collapsed;
        ContentScrollViewer.ScrollToTop();
        SetActiveNav(DashboardNav);
    }

    private void OnHistoryNavClick(object sender, RoutedEventArgs e)
    {
        ContentScrollViewer.Visibility = Visibility.Collapsed;
        HistoryScrollViewer.Visibility = Visibility.Visible;
        SetActiveNav(HistoryNav);
    }

    private void OnTrendRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrendRangeSelector?.SelectedItem is not ComboBoxItem item
            || !Enum.TryParse(item.Tag?.ToString(), ignoreCase: true, out TrendRange range)) return;
        _trendRange = range;
        DrawTrendChart();
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;
        UpdateActiveNavigation();
    }

    private void SetActiveNav(Button active)
    {
        if (ReferenceEquals(_activeNav, active)) return;
        _activeNav = active;
        foreach (var button in new[] { DashboardNav, HistoryNav })
        {
            button.Background = button == active ? new SolidColorBrush(Color.FromRgb(18, 49, 72)) : Brushes.Transparent;
            button.Foreground = button == active ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromRgb(185, 199, 219));
            button.FontWeight = button == active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void UpdateActiveNavigation()
    {
        if (!IsLoaded) return;
        SetActiveNav(ContentScrollViewer.Visibility == Visibility.Visible ? DashboardNav : HistoryNav);
    }

    private void StartAutoRefresh()
    {
        _refreshTimer.Start();
        try
        {
            Directory.CreateDirectory(_store.DirectoryPath);
            _watcher = new FileSystemWatcher(_store.DirectoryPath, "usage-events*.ndjson")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnDataFileChanged;
            _watcher.Created += OnDataFileChanged;
            _watcher.Renamed += OnDataFileRenamed;

            _balanceWatcher = new FileSystemWatcher(_store.DirectoryPath, BalanceUsageSnapshot.FileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _balanceWatcher.Changed += OnDataFileChanged;
            _balanceWatcher.Created += OnDataFileChanged;
            _balanceWatcher.Renamed += OnDataFileRenamed;

            _serverUsageWatcher = new FileSystemWatcher(_store.DirectoryPath, ServerUsageSummarySnapshot.FileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _serverUsageWatcher.Changed += OnDataFileChanged;
            _serverUsageWatcher.Created += OnDataFileChanged;
            _serverUsageWatcher.Renamed += OnDataFileRenamed;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void StopAutoRefresh()
    {
        _refreshTimer.Stop();
        _clockTimer.Stop();
        _fileRefreshTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _balanceWatcher?.Dispose();
        _balanceWatcher = null;
        _serverUsageWatcher?.Dispose();
        _serverUsageWatcher = null;
    }

    private void OnDataFileChanged(object sender, FileSystemEventArgs e) => QueueFileRefresh();

    private void OnDataFileRenamed(object sender, RenamedEventArgs e) => QueueFileRefresh();

    private void QueueFileRefresh()
    {
        if (_fileRefreshPending) return;
        _fileRefreshPending = true;
        Dispatcher.BeginInvoke(new Action(() => _fileRefreshTimer.Start()), DispatcherPriority.Background);
    }

    private void Refresh()
    {
        _nextRefreshAt = DateTimeOffset.Now.Add(AutoRefreshInterval);
        var events = _store.ReadAll();
        _lastEvents = events;
        _lastReport = UsageReport.Build(events, DateTimeOffset.Now);
        _lastBalanceUsage = BalanceUsageSnapshot.Read(_store.DirectoryPath, DateTimeOffset.Now);
        _lastServerUsage = ServerUsageSummarySnapshot.Read(_store.DirectoryPath, DateTimeOffset.Now);
        var report = _lastReport;
        TwentyFourHourTokens.Text = report.TwentyFourHours.HasTokenData ? UsageFormatting.Tokens(report.TwentyFourHours.TotalTokens) : "未上报";
        AllTimeTokens.Text = report.AllTime.HasTokenData ? UsageFormatting.Tokens(report.AllTime.TotalTokens) : "未上报";
        AllTimeTokenBreakdown.Text = report.AllTime.HasTokenData
            ? $"输入 {UsageFormatting.Tokens(report.AllTime.InputTokens)} / 输出 {UsageFormatting.Tokens(report.AllTime.OutputTokens)}"
            : "输入 未上报 / 输出 未上报";
        CacheHit.Text = report.AllTime.InputTokens <= 0 ? "—" : $"{report.AllTime.CacheHitPercent:0.#}%";
        Requests.Text = report.AllTime.Requests.ToString("N0");
        SuccessRate.Text = $"成功率 {report.AllTime.SuccessPercent:0.#}%";
        TodayUsage.Text = _lastBalanceUsage.HasData
            ? UsageFormatting.Currency(_lastBalanceUsage.TodayUsage, _lastBalanceUsage.Currency)
            : "暂无数据";
        TodayUsageHint.Text = _lastBalanceUsage.HasData
            ? "主程序余额变化记录"
            : "主程序尚无余额记录";
        ServerUsageSummary.Text = _lastServerUsage.HasData
            ? UsageFormatting.Currency(_lastServerUsage.TodayAmount, _lastServerUsage.Currency)
            : "暂无数据";
        ServerUsageSummaryHint.Text = _lastServerUsage.HasData
            ? $"/v1/usage 按日汇总 · {_lastServerUsage.Date}"
            : "当前站点尚无汇总额度";
        TotalBalance.Text = _lastBalanceUsage.HasBalanceData
            ? UsageFormatting.Currency(_lastBalanceUsage.TotalBalance, _lastBalanceUsage.BalanceCurrency)
            : "暂无数据";
        TotalBalanceHint.Text = _lastBalanceUsage.HasBalanceData
            ? _lastBalanceUsage.BalanceAccountCount > 1
                ? $"同币种账户合计 · {_lastBalanceUsage.BalanceAccountCount:N0} 个账户"
                : "当前账户余额快照"
            : "主程序尚无余额快照";
        Throughput.Text = UsageFormatting.Rate(report.AllTime.OutputPerSecond);
        AverageDuration.Text = UsageFormatting.Milliseconds(report.AllTime.AverageDurationMs);
        var providers = events.Select(value => value.Provider).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var models = events.Select(value => value.Model).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        ProviderModelCount.Text = $"{providers} / {models}";

        var providerRows = events.GroupBy(value => string.IsNullOrWhiteSpace(value.Provider) ? "Unknown" : value.Provider, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(value => (value.InputTokens ?? 0) + (value.OutputTokens ?? 0)))
            .ThenByDescending(group => group.Count())
            .Take(12)
            .Select((group, index) => new ProviderRow(group.Key, group.ToArray(), events.Count, Palette(index)))
            .ToArray();
        ProviderItems.ItemsSource = providerRows;
        ProviderSummary.Text = providerRows.Length == 0 ? "暂无请求" : $"{providerRows.Length} 个提供方";

        var modelValues = events.Where(value => !string.IsNullOrWhiteSpace(value.Model)).GroupBy(value => value.Model, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Name: group.Key, Events: group.ToArray(), Tokens: group.Sum(value => (value.InputTokens ?? 0) + (value.OutputTokens ?? 0))))
            .OrderByDescending(value => value.Tokens).ThenByDescending(value => value.Events.Length).Take(12).ToArray();
        var modelBasis = modelValues.Sum(value => value.Tokens);
        if (modelBasis <= 0) modelBasis = modelValues.Sum(value => (long)value.Events.Length);
        ModelItems.ItemsSource = modelValues.Select((value, index) => new ModelRow(value.Name, value.Events.Length, value.Tokens, modelBasis, Palette(index + 1))).ToArray();
        ModelEmpty.Visibility = modelValues.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        EventsList.ItemsSource = events.Select(value => new EventRow(value)).ToArray();
        RecentSummary.Text = events.Count == 0 ? "暂无记录" : $"共 {events.Count:N0} 条";
        var hasUsageDetails = events.Any(value =>
            value.InputTokens.HasValue || value.OutputTokens.HasValue || value.CacheReadTokens.HasValue ||
            value.CacheWriteTokens.HasValue || value.DurationMs.HasValue || value.TimeToFirstTokenMs.HasValue ||
            !string.IsNullOrWhiteSpace(value.Model));
        StatusText.Text = events.Count == 0
            ? $"暂无用量事件 · 请确认主程序已启用 AI 任务联动 · 数据目录：{_store.DirectoryPath}"
            : hasUsageDetails
                ? $"已读取 {events.Count:N0} 条事件 · 最后刷新：{DateTime.Now:HH:mm:ss}"
                : $"已读取 {events.Count:N0} 条生命周期事件；客户端尚未上报 Token、模型和耗时。若客户端支持计数，请通过 Usage.v1 或 balancepet-usage.ps1 上报 · 最后刷新：{DateTime.Now:HH:mm:ss}";
        UpdateAutoRefreshStatus();
        DrawTrendChart();
        Dispatcher.BeginInvoke(UpdateActiveNavigation, DispatcherPriority.Background);
    }

    private void UpdateAutoRefreshStatus()
    {
        if (AutoRefreshStatus is null) return;
        var remaining = Math.Max(0, (int)Math.Ceiling((_nextRefreshAt - DateTimeOffset.Now).TotalSeconds));
        AutoRefreshStatus.Text = $"自动同步 · {remaining} 秒后 · {DateTime.Now:HH:mm:ss}";
    }

    private static void RequestCoreRefresh()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(@"Local\BalancePet.UsageAnalytics.Refresh.v1");
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => DrawTrendChart();

    private void DrawTrendChart()
    {
        if (TrendChart is null || _lastReport is null || TrendChart.ActualWidth < 80 || TrendChart.ActualHeight < 80) return;
        TrendChart.Children.Clear();
        var events = _lastEvents;
        var now = DateTimeOffset.Now;
        var buckets = CreateTrendBuckets(_trendRange, events, now);
        var points = buckets.Select(bucket =>
        {
            var values = events.Where(value => value.OccurredAt.LocalDateTime >= bucket.Start && value.OccurredAt.LocalDateTime < bucket.End).ToArray();
            var input = values.Sum(value => value.InputTokens ?? 0);
            var cacheRead = values.Sum(value => value.CacheReadTokens ?? 0);
            return new TrendPoint(
                bucket.Start,
                bucket.Label,
                Math.Max(0, input - cacheRead),
                values.Sum(value => value.OutputTokens ?? 0),
                values.Sum(value => value.CacheWriteTokens ?? 0),
                cacheRead,
                input <= 0 ? 0 : Math.Clamp(cacheRead * 100d / input, 0, 100),
                values.Length);
        }).ToArray();
        var hasTokens = points.Any(value => value.HasTokenData);
        TrendEmpty.Visibility = hasTokens ? Visibility.Collapsed : Visibility.Visible;
        var plotLeft = 34d;
        var plotRight = Math.Max(plotLeft + 40, TrendChart.ActualWidth - 42);
        var plotTop = 10d;
        var plotBottom = Math.Max(plotTop + 30, TrendChart.ActualHeight - 28);
        var maxTokens = Math.Max(1, points.Max(value => value.MaxTokenSeries));
        DrawGridLines(plotLeft, plotRight, plotTop, plotBottom, maxTokens);
        DrawDateLabels(points, plotLeft, plotRight, plotBottom);
        if (!hasTokens) return;

        DrawTokenSeries(points, value => value.InputTokens, (Brush)FindResource("BlueBrush"), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawTokenSeries(points, value => value.OutputTokens, (Brush)FindResource("AccentBrush"), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawTokenSeries(points, value => value.CacheCreationTokens, (Brush)FindResource("OrangeBrush"), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawTokenSeries(points, value => value.CacheReadTokens, new SolidColorBrush(Color.FromRgb(21, 199, 215)), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawRateSeries(points, (Brush)FindResource("PurpleBrush"), plotLeft, plotRight, plotTop, plotBottom);
    }

    private static IReadOnlyList<TrendBucket> CreateTrendBuckets(TrendRange range, IReadOnlyList<UsageEvent> events, DateTimeOffset now)
    {
        var localNow = now.LocalDateTime;
        switch (range)
        {
            case TrendRange.Last24Hours:
            {
                var end = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0);
                var start = end.AddHours(-23);
                return Enumerable.Range(0, 24).Select(index =>
                {
                    var bucketStart = start.AddHours(index);
                    return new TrendBucket(bucketStart, bucketStart.AddHours(1), bucketStart.ToString("HH:mm"));
                }).ToArray();
            }
            case TrendRange.Last30Days:
            {
                var end = localNow.Date;
                var start = end.AddDays(-29);
                return Enumerable.Range(0, 30).Select(index =>
                {
                    var bucketStart = start.AddDays(index);
                    return new TrendBucket(bucketStart, bucketStart.AddDays(1), bucketStart.ToString("MM-dd"));
                }).ToArray();
            }
            case TrendRange.Last90Days:
            {
                var end = localNow.Date.AddDays(1);
                var start = end.AddDays(-91);
                return Enumerable.Range(0, 13).Select(index =>
                {
                    var bucketStart = start.AddDays(index * 7);
                    return new TrendBucket(bucketStart, bucketStart.AddDays(7), bucketStart.ToString("MM-dd"));
                }).ToArray();
            }
            case TrendRange.AllTime:
            {
                var first = events.Count == 0 ? localNow.Date.AddMonths(-11) : events.Min(value => value.OccurredAt.LocalDateTime).Date;
                var start = new DateTime(first.Year, first.Month, 1);
                var end = new DateTime(localNow.Year, localNow.Month, 1).AddMonths(1);
                var count = Math.Max(1, (end.Year - start.Year) * 12 + end.Month - start.Month);
                return Enumerable.Range(0, count).Select(index =>
                {
                    var bucketStart = start.AddMonths(index);
                    return new TrendBucket(bucketStart, bucketStart.AddMonths(1), bucketStart.ToString("yyyy-MM"));
                }).ToArray();
            }
            default:
            {
                var end = localNow.Date;
                var start = end.AddDays(-6);
                return Enumerable.Range(0, 7).Select(index =>
                {
                    var bucketStart = start.AddDays(index);
                    return new TrendBucket(bucketStart, bucketStart.AddDays(1), bucketStart.ToString("MM-dd"));
                }).ToArray();
            }
        }
    }

    private void DrawGridLines(double plotLeft, double plotRight, double plotTop, double plotBottom, long maxTokens)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(90, 70, 100, 130));
        var muted = (Brush)FindResource("MutedBrush");
        for (var index = 0; index < 3; index++)
        {
            var fraction = index / 2d;
            var y = plotBottom - (plotBottom - plotTop) * fraction;
            TrendChart.Children.Add(new Line
            {
                X1 = plotLeft,
                X2 = plotRight,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeDashArray = new DoubleCollection { 2, 3 },
                StrokeThickness = 1
            });
            AddChartLabel(UsageFormatting.Tokens((long)Math.Round(maxTokens * fraction)), 2, y - 8, muted, 10, 30);
            AddChartLabel($"{fraction * 100:0}%", plotRight + 5, y - 8, muted, 10, 38);
        }
    }

    private void DrawDateLabels(IReadOnlyList<TrendPoint> points, double plotLeft, double plotRight, double plotBottom)
    {
        if (points.Count == 0) return;
        var width = plotRight - plotLeft;
        var step = Math.Max(1, (int)Math.Ceiling(points.Count / 7d));
        var indexes = Enumerable.Range(0, points.Count).Where(index => index % step == 0).ToList();
        if (!indexes.Contains(points.Count - 1)) indexes.Add(points.Count - 1);
        foreach (var index in indexes.Distinct())
        {
            var x = plotLeft + width * index / Math.Max(1, points.Count - 1);
            AddChartLabel(points[index].Label, x - 22, plotBottom + 8, (Brush)FindResource("MutedBrush"), 10, 48);
        }
    }

    private void DrawTokenSeries(
        IReadOnlyList<TrendPoint> points,
        Func<TrendPoint, long> selector,
        Brush color,
        long maxTokens,
        double plotLeft,
        double plotRight,
        double plotTop,
        double plotBottom)
    {
        var line = new Polyline
        {
            Stroke = color,
            StrokeThickness = 2.1,
            Opacity = 0.95,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        var width = plotRight - plotLeft;
        var height = plotBottom - plotTop;
        for (var index = 0; index < points.Count; index++)
        {
            var x = plotLeft + width * index / Math.Max(1, points.Count - 1);
            var value = Math.Max(0, selector(points[index]));
            var y = plotBottom - height * value / maxTokens;
            line.Points.Add(new Point(x, y));
            if (value <= 0) continue;
            var dot = new Ellipse { Width = 6, Height = 6, Fill = color, Stroke = (Brush)FindResource("PanelBrush"), StrokeThickness = 1 };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            TrendChart.Children.Add(dot);
        }
        Canvas.SetZIndex(line, 2);
        TrendChart.Children.Add(line);
    }

    private void DrawRateSeries(IReadOnlyList<TrendPoint> points, Brush color, double plotLeft, double plotRight, double plotTop, double plotBottom)
    {
        var line = new Polyline
        {
            Stroke = color,
            StrokeThickness = 2.2,
            Opacity = 0.98,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        var width = plotRight - plotLeft;
        var height = plotBottom - plotTop;
        for (var index = 0; index < points.Count; index++)
        {
            var x = plotLeft + width * index / Math.Max(1, points.Count - 1);
            var y = plotBottom - height * Math.Clamp(points[index].CacheHitRate, 0, 100) / 100d;
            line.Points.Add(new Point(x, y));
            if (points[index].InputTokens <= 0) continue;
            var dot = new Ellipse { Width = 6, Height = 6, Fill = color, Stroke = (Brush)FindResource("PanelBrush"), StrokeThickness = 1 };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            TrendChart.Children.Add(dot);
        }
        Canvas.SetZIndex(line, 3);
        TrendChart.Children.Add(line);
    }

    private void AddChartLabel(string text, double left, double top, Brush brush, double fontSize, double width)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = fontSize,
            Width = width,
            TextAlignment = TextAlignment.Left,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        Canvas.SetZIndex(label, 4);
        TrendChart.Children.Add(label);
    }

    private void OnOpenDataClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_store.DirectoryPath);
            Process.Start(new ProcessStartInfo("explorer.exe", _store.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    private static Brush Palette(int index) => (index % 5) switch
    {
        0 => new SolidColorBrush(Color.FromRgb(45, 225, 194)),
        1 => new SolidColorBrush(Color.FromRgb(110, 168, 255)),
        2 => new SolidColorBrush(Color.FromRgb(181, 134, 255)),
        3 => new SolidColorBrush(Color.FromRgb(244, 185, 66)),
        _ => new SolidColorBrush(Color.FromRgb(255, 112, 134))
    };

    private sealed record TrendPoint(
        DateTime Date,
        string Label,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens,
        double CacheHitRate,
        int Requests)
    {
        public long MaxTokenSeries => Math.Max(Math.Max(InputTokens, OutputTokens), Math.Max(CacheCreationTokens, CacheReadTokens));
        public bool HasTokenData => MaxTokenSeries > 0;
    }

    private sealed record TrendBucket(DateTime Start, DateTime End, string Label);

    private enum TrendRange
    {
        Last24Hours,
        Last7Days,
        Last30Days,
        Last90Days,
        AllTime
    }

    private sealed class ProviderRow
    {
        public ProviderRow(string name, IReadOnlyList<UsageEvent> events, int totalEvents, Brush brush)
        {
            Name = name; Brush = brush;
            RequestText = $"{events.Count:N0} 次请求 · 成功 {events.Count(value => value.Success == true):N0}";
            var tokens = events.Sum(value => (value.InputTokens ?? 0) + (value.OutputTokens ?? 0));
            TokenText = tokens > 0 ? $"{UsageFormatting.Tokens(tokens)} Token" : "Token 未上报";
            ShareText = totalEvents == 0 ? "—" : $"{events.Count * 100d / totalEvents:0.#}%";
        }
        public string Name { get; }
        public string RequestText { get; }
        public string TokenText { get; }
        public string ShareText { get; }
        public Brush Brush { get; }
    }

    private sealed class ModelRow
    {
        public ModelRow(string name, int requests, long tokens, long basis, Brush brush)
        {
            Name = name; Brush = brush; Percent = basis <= 0 ? 0 : Math.Clamp((tokens > 0 ? tokens : requests) * 100d / basis, 0, 100);
            TokenText = tokens > 0 ? $"{UsageFormatting.Tokens(tokens)} · {requests:N0} 次" : $"Token 未上报 · {requests:N0} 次";
        }
        public string Name { get; }
        public string TokenText { get; }
        public double Percent { get; }
        public Brush Brush { get; }
    }

    private sealed class EventRow
    {
        private readonly UsageEvent _event;
        public EventRow(UsageEvent value)
        {
            _event = value;
            Details = value.Details.Select(detail => new DetailRow(detail)).ToArray();
        }
        public string OccurredAtText => _event.OccurredAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
        public string Provider => _event.Provider;
        public string Model => string.IsNullOrWhiteSpace(_event.Model) ? "模型未上报" : _event.Model;
        public string ReasoningText
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_event.ReasoningEffort)) return $"思考强度 {_event.ReasoningEffort}";
                var values = _event.Details.Select(detail => detail.ReasoningEffort)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return values.Length switch { 1 => $"思考强度 {values[0]}", > 1 => "思考强度 多种", _ => "思考强度 未上报" };
            }
        }
        public string InputText => _event.InputTokens is null ? "输入给模型 —" : $"输入给模型 {UsageFormatting.Tokens(_event.InputTokens.Value)}";
        public string OutputText => _event.OutputTokens is null ? "模型输出 —" : $"模型输出 {UsageFormatting.Tokens(_event.OutputTokens.Value)}";
        public string CacheReadText => _event.CacheReadTokens is null ? "缓存读取 —" : $"缓存读取 {UsageFormatting.Tokens(_event.CacheReadTokens.Value)}";
        public string CacheWriteText => _event.CacheWriteTokens is null ? "缓存写入 —" : $"缓存写入 {UsageFormatting.Tokens(_event.CacheWriteTokens.Value)}";
        public string CostText => _event.Cost is null ? "未上报" : $"{_event.Cost:0.########} {(_event.Currency.Length == 0 ? "USD" : _event.Currency)}";
        public string DurationText => _event.DurationMs is null ? "耗时 —" : $"耗时 {UsageFormatting.Milliseconds(_event.DurationMs)}";
        public string DetailsCaption => Details.Count == 0 ? "查看中转站请求明细 · 暂无可关联记录" : $"查看本次任务的 {Details.Count} 次中转站请求";
        public IReadOnlyList<DetailRow> Details { get; }
        public string StatusText => _event.Success switch { true => "成功", false => "失败", _ => "未知" };
        public Brush StatusBrush => _event.Success switch { true => new SolidColorBrush(Color.FromRgb(45, 225, 194)), false => new SolidColorBrush(Color.FromRgb(255, 112, 134)), _ => (Brush)Application.Current.FindResource("MutedBrush") };

        public sealed class DetailRow(UsageEventDetail value)
        {
            public string TimeText => value.OccurredAt.ToLocalTime().ToString("HH:mm:ss");
            public string ModelEffortText => string.IsNullOrWhiteSpace(value.ReasoningEffort) ? value.Model : $"{value.Model} · {value.ReasoningEffort}";
            public string InputText => value.InputTokens is null ? "输入 —" : $"输入 {UsageFormatting.Tokens(value.InputTokens.Value)}";
            public string OutputText => value.OutputTokens is null ? "输出 —" : $"输出 {UsageFormatting.Tokens(value.OutputTokens.Value)}";
            public string CacheText => value.CacheReadTokens is null ? "缓存 —" : $"缓存 {UsageFormatting.Tokens(value.CacheReadTokens.Value)}";
            public string CostText => value.Cost is null ? "费用未上报" : $"{value.Cost:0.########} {(value.Currency.Length == 0 ? "USD" : value.Currency)}";
        }
    }
}
