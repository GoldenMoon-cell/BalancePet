using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Input;

namespace BalancePet.UsageAnalytics;

public partial class MainWindow : Window
{
    private readonly UsageEventStore _store;
    /// <summary>
    /// Debounces the file watchers. Updates come from watching the data files and
    /// from the manual refresh button, never from a timer: the host writes a file
    /// for every new record, so polling was pure overhead.
    /// </summary>
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
    private EventRow? _selectedEvent;
    private IReadOnlyList<TrendPoint> _trendPoints = Array.Empty<TrendPoint>();
    private double _trendPlotLeft;
    private double _trendPlotRight;
    private Border? _trendTooltip;
    private Line? _trendHoverLine;
    /// <summary>Selected client, empty for every client.</summary>
    private string _providerFilter = "";
    /// <summary>Selected account, empty for the combined view.</summary>
    private string _accountFilter = "";
    /// <summary>
    /// Signatures of what the two selectors currently show, so a periodic
    /// refresh rebuilds them only when their options really changed. Rebuilding
    /// unconditionally would fight the user's selection every 60 seconds.
    /// </summary>
    private string _providerFilterSignature = "";
    private string _accountSelectorSignature = "";
    private bool _suppressFilterRefresh;
    private IReadOnlyDictionary<string, string> _accountNames = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, AccountDirectory.AccountInfo> _accounts = new Dictionary<string, AccountDirectory.AccountInfo>(StringComparer.OrdinalIgnoreCase);

    public MainWindow(string dataDirectory)
    {
        InitializeComponent();
        _store = new UsageEventStore(dataDirectory);
        _fileRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _fileRefreshTimer.Tick += (_, _) =>
        {
            _fileRefreshTimer.Stop();
            _fileRefreshPending = false;
            Refresh();
        };
        Loaded += (_, _) =>
        {
            StartWatching();
            DrawTrendChart();
            UpdateActiveNavigation();
            // One request to the host so opening the window shows current
            // balances. This is not a poll: it happens once per open.
            RequestCoreRefresh();
        };
        Closed += (_, _) => StopWatching();
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
        HideEventDetail(immediate: true);
        ContentScrollViewer.Visibility = Visibility.Visible;
        HistoryScrollViewer.Visibility = Visibility.Collapsed;
        ContentScrollViewer.ScrollToTop();
        SetActiveNav(DashboardNav);
    }

    private void OnHistoryNavClick(object sender, RoutedEventArgs e)
    {
        HideEventDetail(immediate: true);
        ContentScrollViewer.Visibility = Visibility.Collapsed;
        HistoryScrollViewer.Visibility = Visibility.Visible;
        SetActiveNav(HistoryNav);
    }

    private void OnEventCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EventRow row })
        {
            e.Handled = true;
            ShowEventDetail(row);
        }
    }

    private void OnEventSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventsList.SelectedItem is EventRow row && !ReferenceEquals(_selectedEvent, row))
            ShowEventDetail(row);
    }

    private void OnDetailBackClick(object sender, RoutedEventArgs e) => HideEventDetail();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && HistoryDetailView.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            HideEventDetail();
        }
    }

    private void ShowEventDetail(EventRow row)
    {
        _selectedEvent = row;
        EventsList.SelectedItem = row;
        HistoryDetailView.DataContext = row;
        ContentScrollViewer.Visibility = Visibility.Collapsed;
        HistoryScrollViewer.Visibility = Visibility.Collapsed;
        HistoryDetailView.Visibility = Visibility.Visible;
        HistoryDetailView.Opacity = 0;
        HistoryDetailScale.ScaleX = 0.97;
        HistoryDetailScale.ScaleY = 0.97;
        // Three panels describe individual relay requests. A balance-derived cost
        // has none, so showing them would only produce empty placeholders that
        // look like a parsing failure.
        var perRequestPanels = row.IsBalanceDelta ? Visibility.Collapsed : Visibility.Visible;
        CostTimelinePanel.Visibility = perRequestPanels;
        TokenTrendPanel.Visibility = perRequestPanels;
        RelayDetailPanel.Visibility = perRequestPanels;
        DrawDetailCharts(row);

        var duration = new Duration(TimeSpan.FromMilliseconds(230));
        HistoryDetailView.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        HistoryDetailScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        HistoryDetailScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Dispatcher.BeginInvoke(() => HistoryDetailScrollViewer.ScrollToTop(), DispatcherPriority.Background);
    }

    private void HideEventDetail(bool immediate = false)
    {
        if (HistoryDetailView.Visibility != Visibility.Visible && !immediate) return;
        if (immediate)
        {
            HistoryDetailView.BeginAnimation(UIElement.OpacityProperty, null);
            HistoryDetailView.Visibility = Visibility.Collapsed;
            _selectedEvent = null;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            HistoryDetailView.Visibility = Visibility.Collapsed;
            HistoryScrollViewer.Visibility = Visibility.Visible;
            HistoryDetailView.Opacity = 0;
            _selectedEvent = null;
            EventsList.SelectedItem = null;
        };
        HistoryDetailView.BeginAnimation(UIElement.OpacityProperty, fade);
        HistoryDetailScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, duration));
        HistoryDetailScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, duration));
    }

    private void OnDetailChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_selectedEvent is not null) DrawDetailCharts(_selectedEvent);
    }

    private void DrawDetailCharts(EventRow row)
    {
        DrawTokenComposition(row);
        DrawCostTimeline(row);
        DrawTokenTrend(row);
    }

    private void DrawTokenComposition(EventRow row)
    {
        TokenCompositionChart.Children.Clear();
        var values = new[]
        {
            (Name: "输入", Value: row.InputTokensValue, Brush: (Brush)FindResource("BlueBrush")),
            (Name: "缓存读取", Value: row.CacheReadTokensValue, Brush: new SolidColorBrush(Color.FromRgb(21, 199, 215))),
            (Name: "输出", Value: row.OutputTokensValue, Brush: (Brush)FindResource("AccentBrush")),
            (Name: "缓存写入", Value: row.CacheWriteTokensValue, Brush: (Brush)FindResource("OrangeBrush"))
        };
        var max = values.Max(value => value.Value);
        TokenChartEmpty.Visibility = max <= 0 ? Visibility.Visible : Visibility.Collapsed;
        if (max <= 0) return;

        var width = Math.Max(180, TokenCompositionChart.ActualWidth);
        var labelWidth = 58d;
        var valueWidth = 64d;
        var barWidth = Math.Max(40, width - labelWidth - valueWidth);
        for (var index = 0; index < values.Length; index++)
        {
            var item = values[index];
            var y = index * 17d;
            AddChartText(TokenCompositionChart, item.Name, 0, y - 2, (Brush)FindResource("MutedBrush"), 10);
            var track = new Border { Width = barWidth, Height = 7, Background = new SolidColorBrush(Color.FromRgb(32, 52, 76)), CornerRadius = new CornerRadius(4) };
            Canvas.SetLeft(track, labelWidth); Canvas.SetTop(track, y + 1); TokenCompositionChart.Children.Add(track);
            var fill = new Border { Width = Math.Max(item.Value > 0 ? 3 : 0, barWidth * item.Value / max), Height = 7, Background = item.Brush, CornerRadius = new CornerRadius(4) };
            Canvas.SetLeft(fill, labelWidth); Canvas.SetTop(fill, y + 1); TokenCompositionChart.Children.Add(fill);
            AddChartText(TokenCompositionChart, item.Value > 0 ? UsageFormatting.Tokens(item.Value) : "—", labelWidth + barWidth + 7, y - 2, (Brush)FindResource("TextBrush"), 10);
        }
    }

    private void DrawCostTimeline(EventRow row)
    {
        CostTimelineChart.Children.Clear();
        var values = row.Details.Where(detail => detail.CostValue.HasValue).ToArray();
        CostChartEmpty.Visibility = values.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (values.Length == 0) return;

        var width = Math.Max(220, CostTimelineChart.ActualWidth);
        var height = Math.Max(54, CostTimelineChart.ActualHeight);
        var max = Math.Max(0.000001, values.Max(value => value.CostValue!.Value));
        var left = 8d;
        var bottom = height - 16;
        var plotWidth = Math.Max(80, width - 16);
        var step = plotWidth / Math.Max(1, values.Length);
        var barWidth = Math.Max(3, Math.Min(18, step * 0.58));
        var baseline = new Border { Width = plotWidth, Height = 1, Background = (Brush)FindResource("PanelBorderStrongBrush") };
        Canvas.SetLeft(baseline, left); Canvas.SetTop(baseline, bottom); CostTimelineChart.Children.Add(baseline);
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            var barHeight = Math.Max(3, (height - 28) * value.CostValue!.Value / max);
            var bar = new Border
            {
                Width = barWidth,
                Height = barHeight,
                Background = (Brush)FindResource("PurpleBrush"),
                CornerRadius = new CornerRadius(3),
                ToolTip = $"{value.TimeText} · {value.CostText}"
            };
            Canvas.SetLeft(bar, left + index * step + (step - barWidth) / 2); Canvas.SetTop(bar, bottom - barHeight); CostTimelineChart.Children.Add(bar);
        }
        AddChartText(CostTimelineChart, UsageFormatting.Currency(max, row.Currency), 8, 0, (Brush)FindResource("MutedBrush"), 10);
        AddChartText(CostTimelineChart, values.Length == 1 ? "1 次请求" : $"{values.Length} 次请求", Math.Max(8, width - 62), bottom + 4, (Brush)FindResource("MutedBrush"), 10);
    }

    private void DrawTokenTrend(EventRow row)
    {
        TokenTrendChart.Children.Clear();
        var values = row.Details.Where(detail => detail.HasTokenData).ToArray();
        TokenTrendEmpty.Visibility = values.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (values.Length == 0) return;

        var width = Math.Max(420, TokenTrendChart.ActualWidth);
        var height = Math.Max(120, TokenTrendChart.ActualHeight);
        var left = 44d;
        var right = 12d;
        var top = 14d;
        var bottom = height - 25d;
        var plotWidth = Math.Max(120, width - left - right);
        var plotHeight = Math.Max(50, bottom - top);
        var max = Math.Max(1L, values.SelectMany(value => new long?[] { value.InputValue, value.CacheValue, value.OutputValue })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max());

        foreach (var fraction in new[] { 0d, 0.5d, 1d })
        {
            var y = bottom - plotHeight * fraction;
            var grid = new Border
            {
                Width = plotWidth,
                Height = 1,
                Background = (Brush)FindResource("PanelBorderStrongBrush"),
                Opacity = fraction == 0 ? 0.9 : 0.55
            };
            Canvas.SetLeft(grid, left);
            Canvas.SetTop(grid, y);
            TokenTrendChart.Children.Add(grid);
            AddChartText(TokenTrendChart, UsageFormatting.Tokens((long)(max * fraction)), 0, y - 7, (Brush)FindResource("MutedBrush"), 10);
        }

        DrawTokenTrendSeries(values, detail => detail.InputValue, (Brush)FindResource("BlueBrush"), "输入", max, left, plotWidth, top, bottom);
        DrawTokenTrendSeries(values, detail => detail.CacheValue, new SolidColorBrush(Color.FromRgb(21, 199, 215)), "缓存读取", max, left, plotWidth, top, bottom);
        DrawTokenTrendSeries(values, detail => detail.OutputValue, (Brush)FindResource("AccentBrush"), "输出", max, left, plotWidth, top, bottom);

        var labelIndexes = values.Length switch
        {
            1 => new[] { 0 },
            2 => new[] { 0, 1 },
            _ => new[] { 0, values.Length / 2, values.Length - 1 }
        };
        foreach (var index in labelIndexes.Distinct())
        {
            var x = left + plotWidth * index / Math.Max(1, values.Length - 1);
            var labelLeft = Math.Clamp(x - 28, left, Math.Max(left, width - 58));
            AddChartText(TokenTrendChart, values[index].TimeText, labelLeft, bottom + 7, (Brush)FindResource("MutedBrush"), 10);
        }
    }

    private void DrawTokenTrendSeries(
        IReadOnlyList<EventRow.DetailRow> values,
        Func<EventRow.DetailRow, long?> selector,
        Brush color,
        string seriesName,
        long max,
        double left,
        double plotWidth,
        double top,
        double bottom)
    {
        var lines = new List<Polyline>();
        Polyline? line = null;
        var plotHeight = bottom - top;
        for (var index = 0; index < values.Count; index++)
        {
            var x = left + plotWidth * index / Math.Max(1, values.Count - 1);
            var value = selector(values[index]);
            if (!value.HasValue)
            {
                if (line is not null) lines.Add(line);
                line = null;
                continue;
            }
            line ??= new Polyline
            {
                Stroke = color,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                ToolTip = seriesName
            };
            var numericValue = Math.Max(0, value.Value);
            var y = bottom - plotHeight * numericValue / max;
            line.Points.Add(new Point(x, y));
            if (numericValue <= 0) continue;
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = color,
                Stroke = (Brush)FindResource("PanelBrush"),
                StrokeThickness = 1,
                ToolTip = $"{values[index].TimeText} · {seriesName} {UsageFormatting.Tokens(numericValue)}"
            };
            Canvas.SetLeft(dot, x - 3.5);
            Canvas.SetTop(dot, y - 3.5);
            Canvas.SetZIndex(dot, 3);
            TokenTrendChart.Children.Add(dot);
        }
        if (line is not null) lines.Add(line);
        foreach (var series in lines)
        {
            Canvas.SetZIndex(series, 2);
            TokenTrendChart.Children.Add(series);
        }
    }

    private static void AddChartText(Canvas canvas, string text, double left, double top, Brush brush, double fontSize)
    {
        var label = new TextBlock { Text = text, Foreground = brush, FontSize = fontSize, IsHitTestVisible = false };
        Canvas.SetLeft(label, left); Canvas.SetTop(label, top); canvas.Children.Add(label);
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

    /// <summary>
    /// Watches the three files the host writes. Every new usage record, balance
    /// snapshot and server summary shows up this way, so the view stays current
    /// without polling anything.
    /// </summary>
    private void StartWatching()
    {
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

    private void StopWatching()
    {
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
        var events = _store.ReadAll();
        _lastEvents = events;
        _lastReport = UsageReport.Build(events, DateTimeOffset.Now);
        _accounts = AccountDirectory.ReadAccounts(_store.DirectoryPath);
        _accountNames = _accounts.ToDictionary(pair => pair.Key, pair => pair.Value.Name, StringComparer.OrdinalIgnoreCase);
        _lastBalanceUsage = BalanceUsageSnapshot.Read(_store.DirectoryPath, DateTimeOffset.Now, _accountFilter);
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
            ? $"服务器统计 · /v1/usage · {_lastServerUsage.Date}"
            : "当前站点尚无今日用量";
        TotalBalance.Text = _lastBalanceUsage.HasBalanceData
            ? UsageFormatting.Currency(_lastBalanceUsage.TotalBalance, _lastBalanceUsage.BalanceCurrency)
            : "暂无数据";
        TotalBalanceHint.Text = _lastBalanceUsage.HasBalanceData
            ? _lastBalanceUsage.BalanceAccountCount > 1
                ? $"同币种账户合计 · {_lastBalanceUsage.BalanceAccountCount:N0} 个账户"
                : "当前账户余额快照"
            : "主程序尚无余额快照";
        TotalUsage.Text = _lastBalanceUsage.HasTotalUsageData
            ? UsageFormatting.Currency(_lastBalanceUsage.TotalUsage, _lastBalanceUsage.Currency)
            : "暂无数据";
        TotalUsageHint.Text = _lastBalanceUsage.HasTotalUsageData ? "余额账本累计" : "等待主程序累计记录";
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

        var eventRows = VisibleEvents(events).Select(value => new EventRow(value, _accounts)).ToArray();
        EventsList.ItemsSource = eventRows;
        if (_selectedEvent is not null)
        {
            var refreshed = eventRows.FirstOrDefault(value => string.Equals(value.EventId, _selectedEvent.EventId, StringComparison.OrdinalIgnoreCase));
            if (refreshed is null) HideEventDetail(immediate: true);
            else
            {
                _selectedEvent = refreshed;
                HistoryDetailView.DataContext = refreshed;
                DrawDetailCharts(refreshed);
            }
        }
        RecentSummary.Text = BuildRecentSummary(events, eventRows.Length);
        var hasUsageDetails = events.Any(value =>
            value.InputTokens.HasValue || value.OutputTokens.HasValue || value.CacheReadTokens.HasValue ||
            value.CacheWriteTokens.HasValue || value.DurationMs.HasValue || value.TimeToFirstTokenMs.HasValue ||
            !string.IsNullOrWhiteSpace(value.Model));
        StatusText.Text = events.Count == 0
            ? $"暂无用量事件 · 请确认主程序已启用 AI 任务联动 · 数据目录：{_store.DirectoryPath}"
            : hasUsageDetails
                ? $"已读取 {events.Count:N0} 条事件 · 最后刷新：{DateTime.Now:HH:mm:ss}"
                : $"已读取 {events.Count:N0} 条生命周期事件；客户端尚未上报 Token、模型和耗时。若客户端支持计数，请通过 Usage.v1 或 balancepet-usage.ps1 上报 · 最后刷新：{DateTime.Now:HH:mm:ss}";
        UpdateProviderFilters(events);
        UpdateAccountSelector(_lastBalanceUsage);
        DrawTrendChart();
        Dispatcher.BeginInvoke(UpdateActiveNavigation, DispatcherPriority.Background);
    }

    /// <summary>Events the history list should show under the current filter.</summary>
    private IReadOnlyList<UsageEvent> VisibleEvents(IReadOnlyList<UsageEvent> events)
        => string.IsNullOrWhiteSpace(_providerFilter)
            ? events
            : events.Where(value => string.Equals(value.Provider, _providerFilter, StringComparison.OrdinalIgnoreCase)).ToArray();

    private string BuildRecentSummary(IReadOnlyList<UsageEvent> events, int visible)
    {
        if (events.Count == 0) return "暂无记录";
        return string.IsNullOrWhiteSpace(_providerFilter)
            ? $"共 {events.Count:N0} 条"
            : $"{_providerFilter} {visible:N0} 条 · 全部 {events.Count:N0} 条";
    }

    /// <summary>
    /// Rebuilds the client chips: one for every provider that actually reported,
    /// plus an "all" chip. A filter whose provider no longer appears resets to
    /// "all" so the list can never be stuck empty with no way back.
    /// </summary>
    private void UpdateProviderFilters(IReadOnlyList<UsageEvent> events)
    {
        var groups = events
            .Select(value => string.IsNullOrWhiteSpace(value.Provider) ? "Unknown" : value.Provider)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_providerFilter.Length > 0 && !groups.Any(group => string.Equals(group.Key, _providerFilter, StringComparison.OrdinalIgnoreCase)))
            _providerFilter = "";

        var signature = $"{_providerFilter}|{string.Join("|", groups.Select(group => $"{group.Key}:{group.Count()}"))}";
        if (signature == _providerFilterSignature) return;
        _providerFilterSignature = signature;

        ProviderFilterPanel.Children.Clear();
        ProviderFilterPanel.Children.Add(BuildFilterChip("全部", events.Count, _providerFilter.Length == 0, ""));
        foreach (var group in groups)
        {
            ProviderFilterPanel.Children.Add(BuildFilterChip(
                group.Key,
                group.Count(),
                string.Equals(group.Key, _providerFilter, StringComparison.OrdinalIgnoreCase),
                group.Key));
        }
    }

    private Button BuildFilterChip(string label, int count, bool active, string provider)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new Border
        {
            Margin = new Thickness(7, 0, 0, 0),
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            Background = active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("PanelAltBrush"),
            Child = new TextBlock
            {
                Text = count.ToString("N0"),
                FontSize = 10,
                Foreground = active ? Brushes.White : (Brush)FindResource("MutedBrush")
            }
        });

        var chip = new Button
        {
            Content = content,
            Tag = provider,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(13, 6, 13, 6),
            FontSize = 12,
            Cursor = Cursors.Hand,
            ToolTip = provider.Length == 0 ? "显示所有客户端的记录" : $"只显示 {provider} 的记录",
            Foreground = active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("MutedBrush"),
            Background = active ? (Brush)FindResource("PanelHoverBrush") : (Brush)FindResource("PanelBrush"),
            BorderBrush = active ? (Brush)FindResource("PanelBorderStrongBrush") : (Brush)FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1)
        };
        chip.Click += OnProviderFilterClick;
        return chip;
    }

    private void OnProviderFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var provider = button.Tag as string ?? "";
        if (string.Equals(provider, _providerFilter, StringComparison.Ordinal)) return;
        _providerFilter = provider;
        ApplyHistoryFilter();
    }

    /// <summary>
    /// Re-renders only the history list after a client filter change. A full
    /// refresh would re-read every file, rebuild the report and redraw the
    /// dashboard charts, none of which the filter affects.
    /// </summary>
    private void ApplyHistoryFilter()
    {
        UpdateProviderFilters(_lastEvents);
        var rows = VisibleEvents(_lastEvents).Select(value => new EventRow(value, _accounts)).ToArray();
        EventsList.ItemsSource = rows;
        RecentSummary.Text = BuildRecentSummary(_lastEvents, rows.Length);
        if (_selectedEvent is not null
            && !rows.Any(row => string.Equals(row.EventId, _selectedEvent.EventId, StringComparison.OrdinalIgnoreCase)))
        {
            HideEventDetail(immediate: true);
        }
    }

    /// <summary>
    /// Fills the account selector from the snapshot and the host's account names.
    /// The combined entry is always first and is the default, so every figure on
    /// the dashboard describes the same scope without the user choosing.
    /// </summary>
    private void UpdateAccountSelector(BalanceUsageSnapshot snapshot)
    {
        var options = new List<KeyValuePair<string, string>> { new("", "全部账户（合计）") };
        foreach (var id in snapshot.Accounts) options.Add(new(id, AccountDirectory.Label(_accountNames, id)));

        if (_accountFilter.Length > 0 && !options.Any(option => string.Equals(option.Key, _accountFilter, StringComparison.OrdinalIgnoreCase)))
            _accountFilter = "";

        var signature = string.Join("|", options.Select(option => $"{option.Key}={option.Value}"));
        if (signature == _accountSelectorSignature) return;
        _accountSelectorSignature = signature;

        _suppressFilterRefresh = true;
        try
        {
            AccountSelector.Items.Clear();
            foreach (var option in options)
            {
                var item = new ComboBoxItem { Tag = option.Key, Content = option.Value };
                AccountSelector.Items.Add(item);
                if (string.Equals(option.Key, _accountFilter, StringComparison.OrdinalIgnoreCase)) AccountSelector.SelectedItem = item;
            }
            if (AccountSelector.SelectedItem is null && AccountSelector.Items.Count > 0) AccountSelector.SelectedIndex = 0;
        }
        finally { _suppressFilterRefresh = false; }
    }

    private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterRefresh) return;
        var selected = (AccountSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (string.Equals(selected, _accountFilter, StringComparison.Ordinal)) return;
        _accountFilter = selected;
        Refresh();
    }

    /// <summary>
    /// Re-reads the data directory on demand. This is the only refresh the user
    /// controls; the file watchers cover everything the host writes on its own.
    /// </summary>
    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        RequestCoreRefresh();
        SpinRefreshIcon();
        Refresh();
    }

    /// <summary>
    /// One full turn of the toolbar icon. A single timed animation rather than a
    /// progress indicator: reading the directory takes well under a frame, so the
    /// spin exists to confirm the click landed, not to report progress.
    /// </summary>
    private void SpinRefreshIcon()
    {
        // The icon's transform is a group of [scale, rotate]; hover drives the
        // scale and this drives the rotate, so neither animation clobbers the
        // other's property.
        if (RefreshIcon.RenderTransform is not TransformGroup group) return;
        if (group.Children.Count < 2 || group.Children[1] is not RotateTransform rotation) return;

        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(560),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
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
        _trendPoints = points;
        var hasTokens = points.Any(value => value.HasTokenData);
        TrendEmpty.Visibility = hasTokens ? Visibility.Collapsed : Visibility.Visible;
        var plotLeft = 34d;
        var plotRight = Math.Max(plotLeft + 40, TrendChart.ActualWidth - 42);
        var plotTop = 10d;
        var plotBottom = Math.Max(plotTop + 30, TrendChart.ActualHeight - 28);
        _trendPlotLeft = plotLeft;
        _trendPlotRight = plotRight;
        HideTrendTooltip();
        var maxTokens = Math.Max(1, points.Max(value => value.MaxTokenSeries));
        DrawGridLines(plotLeft, plotRight, plotTop, plotBottom, maxTokens);
        DrawDateLabels(points, plotLeft, plotRight, plotBottom);
        if (!hasTokens) return;

        DrawTokenSeries(points, value => value.InputTokens, (Brush)FindResource("BlueBrush"), new SolidColorBrush(Color.FromArgb(42, 110, 168, 255)), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawTokenSeries(points, value => value.OutputTokens, (Brush)FindResource("AccentBrush"), new SolidColorBrush(Color.FromArgb(42, 45, 225, 194)), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawTokenSeries(points, value => value.CacheCreationTokens, (Brush)FindResource("OrangeBrush"), new SolidColorBrush(Color.FromArgb(30, 244, 185, 66)), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawTokenSeries(points, value => value.CacheReadTokens, new SolidColorBrush(Color.FromRgb(21, 199, 215)), new SolidColorBrush(Color.FromArgb(38, 21, 199, 215)), maxTokens, plotLeft, plotRight, plotTop, plotBottom);
        DrawRateSeries(points, (Brush)FindResource("PurpleBrush"), plotLeft, plotRight, plotTop, plotBottom);
    }

    private void OnTrendChartMouseMove(object sender, MouseEventArgs e)
    {
        if (_trendPoints.Count == 0 || _trendPlotRight <= _trendPlotLeft) return;
        var position = e.GetPosition(TrendChart);
        var ratio = Math.Clamp((position.X - _trendPlotLeft) / (_trendPlotRight - _trendPlotLeft), 0, 1);
        var index = (int)Math.Round(ratio * Math.Max(0, _trendPoints.Count - 1));
        ShowTrendTooltip(index, position.X, position.Y);
    }

    private void OnTrendChartMouseLeave(object sender, MouseEventArgs e) => HideTrendTooltip();

    private void ShowTrendTooltip(int index, double cursorX, double cursorY)
    {
        if (index < 0 || index >= _trendPoints.Count) return;
        var point = _trendPoints[index];
        _trendTooltip ??= CreateTrendTooltip();
        _trendHoverLine ??= new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(150, 150, 170, 195)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            IsHitTestVisible = false
        };
        if (!TrendChart.Children.Contains(_trendHoverLine))
        {
            Canvas.SetZIndex(_trendHoverLine, 8);
            TrendChart.Children.Add(_trendHoverLine);
        }

        var content = new StackPanel { MinWidth = 176 };
        content.Children.Add(new TextBlock
        {
            Text = FormatTrendTooltipDate(point),
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        AddTrendTooltipLine(content, "Input", UsageFormatting.Tokens(point.InputTokens), (Brush)FindResource("BlueBrush"));
        AddTrendTooltipLine(content, "Output", UsageFormatting.Tokens(point.OutputTokens), (Brush)FindResource("AccentBrush"));
        AddTrendTooltipLine(content, "Cache Creation", UsageFormatting.Tokens(point.CacheCreationTokens), (Brush)FindResource("OrangeBrush"));
        AddTrendTooltipLine(content, "Cache Read", UsageFormatting.Tokens(point.CacheReadTokens), new SolidColorBrush(Color.FromRgb(21, 199, 215)));
        AddTrendTooltipLine(content, "Cache Hit Rate", $"{point.CacheHitRate:0.#}%", (Brush)FindResource("PurpleBrush"));
        AddTrendTooltipLine(content, "Requests", point.Requests.ToString("N0"), (Brush)FindResource("MutedBrush"));
        _trendTooltip.Child = content;
        if (!TrendChart.Children.Contains(_trendTooltip))
        {
            Canvas.SetZIndex(_trendTooltip, 10);
            TrendChart.Children.Add(_trendTooltip);
        }

        var x = Math.Clamp(cursorX, _trendPlotLeft, _trendPlotRight);
        _trendHoverLine.X1 = x;
        _trendHoverLine.X2 = x;
        _trendHoverLine.Y1 = 10;
        _trendHoverLine.Y2 = Math.Max(10, TrendChart.ActualHeight - 28);
        _trendHoverLine.Visibility = Visibility.Visible;
        _trendTooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tooltipWidth = _trendTooltip.DesiredSize.Width;
        var tooltipHeight = _trendTooltip.DesiredSize.Height;
        var left = x + 12;
        if (left + tooltipWidth > TrendChart.ActualWidth - 4) left = x - tooltipWidth - 12;
        var top = cursorY + 14;
        if (top + tooltipHeight > TrendChart.ActualHeight - 4) top = cursorY - tooltipHeight - 14;
        top = Math.Clamp(top, 4d, Math.Max(4d, TrendChart.ActualHeight - tooltipHeight - 4));
        Canvas.SetLeft(_trendTooltip, Math.Max(4, left));
        Canvas.SetTop(_trendTooltip, top);
        _trendTooltip.Visibility = Visibility.Visible;
    }

    private Border CreateTrendTooltip()
        => new()
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 3, 8, 15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 65, 84, 109)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 7, 9, 7),
            IsHitTestVisible = false
        };

    private static void AddTrendTooltipLine(Panel panel, string label, string value, Brush brush)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 10 });
        var valueText = new TextBlock { Text = value, Foreground = brush, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        panel.Children.Add(row);
    }

    private string FormatTrendTooltipDate(TrendPoint point)
        => _trendRange == TrendRange.Last24Hours
            ? point.Date.ToString("yyyy-MM-dd HH:00")
            : point.Date.ToString("yyyy-MM-dd");

    private void HideTrendTooltip()
    {
        if (_trendTooltip is not null) _trendTooltip.Visibility = Visibility.Collapsed;
        if (_trendHoverLine is not null) _trendHoverLine.Visibility = Visibility.Collapsed;
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
        Brush areaBrush,
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
        var area = new Polygon { Fill = areaBrush, StrokeThickness = 0, IsHitTestVisible = false };
        area.Points.Add(new Point(plotLeft, plotBottom));
        for (var index = 0; index < points.Count; index++)
        {
            var x = plotLeft + width * index / Math.Max(1, points.Count - 1);
            var value = Math.Max(0, selector(points[index]));
            var y = plotBottom - height * value / maxTokens;
            line.Points.Add(new Point(x, y));
            area.Points.Add(new Point(x, y));
            if (value <= 0) continue;
            var dot = new Ellipse { Width = 6, Height = 6, Fill = color, Stroke = (Brush)FindResource("PanelBrush"), StrokeThickness = 1 };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            Canvas.SetZIndex(dot, 3);
            TrendChart.Children.Add(dot);
        }
        area.Points.Add(new Point(plotRight, plotBottom));
        Canvas.SetZIndex(area, 1);
        TrendChart.Children.Add(area);
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
        public EventRow(UsageEvent value, IReadOnlyDictionary<string, AccountDirectory.AccountInfo> accounts)
        {
            _event = value;
            Details = value.Details.Select(detail => new DetailRow(detail)).ToArray();
            var accountId = value.AccountId;
            if (string.IsNullOrWhiteSpace(accountId)) return;
            if (accounts.TryGetValue(accountId, out var account))
            {
                AccountLabel = account.Name;
                IsOfficialAccount = account.IsOfficial;
                return;
            }
            AccountLabel = AccountDirectory.Label(accountId);
        }
        /// <summary>
        /// Whether the account billed for this task is a vendor's own API rather than
        /// a relay. Decides how an absent cost reads; see the cost text below.
        /// </summary>
        public bool IsOfficialAccount { get; }
        public string OccurredAtText => _event.OccurredAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
        public string Provider => _event.Provider;
        /// <summary>Account the task billed to, empty when the host did not say.</summary>
        public string AccountLabel { get; }
        /// <summary>Client and account together: which tool ran, on whose key.</summary>
        public string ProviderLine => AccountLabel.Length == 0 ? Provider : $"{Provider} · {AccountLabel}";
        public bool HasAccount => AccountLabel.Length > 0;
        /// <summary>
        /// A balance-derived cost is the whole task's spend, not a per-request
        /// charge, so it is labelled differently and gets a different detail view.
        /// </summary>
        public bool IsBalanceDelta => string.Equals(_event.CostSource, "balance-delta", StringComparison.Ordinal);
        public string CostSourceLabel => _event.CostSource switch
        {
            "relay-log" => "中转站逐次",
            "balance-delta" => "余额变化",
            "client" => "客户端上报",
            _ => ""
        };
        public bool HasCostSource => CostSourceLabel.Length > 0;
        public string CostCaption => IsBalanceDelta ? "任务总消耗" : "消耗额度";
        /// <summary>
        /// A balance-derived figure is a real charge but not a per-request one, so
        /// the detail view says where it came from instead of implying the
        /// precision of a matched relay record.
        /// </summary>
        public string CostNote => IsBalanceDelta
            ? "账户余额在本次任务期间的变化，非逐次计费"
            : DetailsCaption;
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
        // "Unreported" means two different things. A relay has a billing log that
        // either matched or did not, so an absent cost is a failed lookup. An official
        // account has no such log, and its only signal is the balance drop a task
        // caused, so an absent cost usually just means the balance did not move.
        public string CostText => _event.Cost is null ? (IsOfficialAccount ? "官方账户 · 未产生余额变化" : "未上报") : $"{_event.Cost:0.########} {(_event.Currency.Length == 0 ? "USD" : _event.Currency)}";
        public string DurationText => _event.DurationMs is null ? "耗时 —" : $"耗时 {UsageFormatting.Milliseconds(_event.DurationMs)}";
        public string DetailsCaption => Details.Count == 0 ? "查看中转站请求明细 · 暂无可关联记录" : $"查看本次任务的 {Details.Count} 次中转站请求";
        public string Currency => string.IsNullOrWhiteSpace(_event.Currency) ? "USD" : _event.Currency;
        public string EventId => _event.EventId;
        public long InputTokensValue => _event.InputTokens ?? _event.Details.Sum(detail => detail.InputTokens ?? 0);
        public long OutputTokensValue => _event.OutputTokens ?? _event.Details.Sum(detail => detail.OutputTokens ?? 0);
        public long CacheReadTokensValue => _event.CacheReadTokens ?? _event.Details.Sum(detail => detail.CacheReadTokens ?? 0);
        public long CacheWriteTokensValue => _event.CacheWriteTokens ?? 0;
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
            public double? CostValue => value.Cost;
            public long? InputValue => value.InputTokens;
            public long? OutputValue => value.OutputTokens;
            public long? CacheValue => value.CacheReadTokens;
            public bool HasTokenData => value.InputTokens.HasValue || value.OutputTokens.HasValue || value.CacheReadTokens.HasValue;
        }
    }
}
