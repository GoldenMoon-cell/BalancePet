using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BalancePet.Wpf.Models;
using BalancePet.Wpf.Services;

// This project enables both WPF and Windows Forms, and implicit usings brings in both
// namespaces, so these four names are ambiguous at every use. Aliasing them once is
// clearer than qualifying each occurrence, which is what the rest of the file was doing
// before the first build complained about all eight.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace BalancePet.Wpf;

/// <summary>
/// Shows what changed outside a release, and holds the switch that decides whether the
/// pet mentions it.
/// </summary>
/// <remarks>
/// The switch belongs here rather than among the interaction switches in Settings. A
/// notification that arrives unprompted is the one people want to turn off, and they will
/// look for the switch where the notification led them — which is this window. Putting it
/// anywhere else would mean someone annoyed by the bubble has to go hunting for a control
/// they have no reason to associate with it.
/// </remarks>
public partial class NoticeWindow : Window
{
    private readonly SettingsStore _store;
    private readonly PetSettings _settings;
    private readonly HttpClient _http;
    private readonly ThemeExtensionManager _themes = new();
    private bool _busy;

    /// <summary>What was unread when the window opened, so the badge can still say so.</summary>
    private int _unreadAtOpen;

    public NoticeWindow(SettingsStore store, PetSettings settings, HttpClient http)
    {
        _store = store;
        _settings = settings;
        _http = http;
        InitializeComponent();
    }

    private void OnWindowSourceInitialized(object sender, EventArgs e)
    {
        var theme = _themes.GetLatestEnabled(_settings.ThemeId)
            ?? _themes.GetLatestEnabled(ThemeExtensionManager.BundledThemeId)
            ?? _themes.GetLatestEnabled().FirstOrDefault();
        if (theme is null) return;
        WindowThemeService.ApplyResources(this, theme, _settings.ThemeMode);
        if (IsInitialized) WindowThemeService.ApplyBackdropOrFallback(this, _settings.ThemeBackdrop, _settings.ThemeMode);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyLocalization();
        NotifyBox.IsChecked = _settings.NoticesNotify;

        // Read at open, then recorded. The badge still reports what was new this time, and
        // the next launch is quiet -- which is what "you have been told" means.
        _unreadAtOpen = NoticeFeed.NewerThan(_settings.NoticesSeenSeq).Count;
        Rebuild();
        MarkSeen();

        // Deliberately no fetch here. Fetching when this window is opened would fix the
        // symptom and invert the point: the bubble exists so that nobody has to open the
        // changelog to learn there is something in it. Keeping the list current is the pet
        // window's job, on a timer, and 刷新 remains for asking on purpose.
    }

    private void ApplyLocalization()
    {
        AppLocalization.Apply(this, _settings.Language);
        Title = AppLocalization.Text(_settings.Language, "小余额更新记录", "BalancePet changelog");
        RefreshButton.Content = AppLocalization.Text(_settings.Language, "刷新", "Refresh");
        CloseFooterButton.Content = AppLocalization.Text(_settings.Language, "关闭", "Close");
        MinimizeButton.ToolTip = AppLocalization.Text(_settings.Language, "最小化", "Minimize");
        CloseButton.ToolTip = AppLocalization.Text(_settings.Language, "关闭", "Close");
        NotifyBox.Content = AppLocalization.Text(_settings.Language, "有新内容时让桌宠提醒我", "Let the pet mention new entries");
        NotifyHintText.Text = AppLocalization.Text(_settings.Language,
            "关掉之后仍然会记录，只是不再主动弹出气泡。",
            "Entries are still recorded when this is off; the pet simply stops mentioning them.");
        IntroText.Text = AppLocalization.Text(_settings.Language,
            "规范、文档和在线内容的变更会记在这里。主程序与扩展的版本更新由「检查更新」负责，不会重复出现在这里。",
            "Changes to specifications, documents and published content are recorded here. Program and extension releases are handled by \"Check for updates\" and do not appear twice.");
        EmptyText.Text = AppLocalization.Text(_settings.Language,
            "还没有读到更新记录。首次联网后会自动获取；如果一直为空，可以用下面的「刷新」重试。",
            "No entries have been read yet. They are fetched once the program can reach the network; if this stays empty, try Refresh below.");
    }

    private void MarkSeen()
    {
        var newest = NoticeFeed.NewestSeq;
        if (newest == 0 || newest <= _settings.NoticesSeenSeq) return;
        _settings.NoticesSeenSeq = newest;
        SaveQuietly();
    }

    /// <summary>
    /// Saving is best-effort. The window is a changelog; failing to record that it was
    /// read costs one repeated bubble, which is not worth an error dialog.
    /// </summary>
    private void SaveQuietly()
    {
        try { _store.Save(_settings); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void Rebuild()
    {
        NoticeList.Children.Clear();
        var items = NoticeFeed.All;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_unreadAtOpen > 0)
        {
            UnreadBadge.Visibility = Visibility.Visible;
            UnreadBadgeText.Text = AppLocalization.Text(_settings.Language, $"{_unreadAtOpen} 条新内容", $"{_unreadAtOpen} new");
        }
        else
        {
            UnreadBadge.Visibility = Visibility.Collapsed;
        }

        // Newest first, and only the ones published after the last time this window was
        // opened are marked. Marking by age would drift; marking by the recorded watermark
        // means the mark matches what the bubble was about.
        var index = 0;
        foreach (var item in items)
        {
            var entry = BuildEntry(item, item.Seq > _settings.NoticesSeenSeq);
            // Staggered, because a list that fades in as one block reads as the window
            // flickering rather than as entries arriving. Capped so a long history does not
            // take a second and a half to appear.
            if (_settings.NavigationAnimations)
            {
                entry.Opacity = 0;
                entry.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                    TimeSpan.FromMilliseconds(180))
                {
                    BeginTime = TimeSpan.FromMilliseconds(Math.Min(index, 8) * 35)
                });
            }
            NoticeList.Children.Add(entry);
            index++;
        }
    }

    private UIElement BuildEntry(NoticeItem item, bool isNew)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (isNew)
        {
            header.Children.Add(new Border
            {
                Background = (Brush)FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = AppLocalization.Text(_settings.Language, "新", "New"),
                    Foreground = Brushes.White,
                    FontSize = 10
                }
            });
        }
        header.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        text.Children.Add(header);

        var meta = $"{item.Date}　·　{item.Area}";
        text.Children.Add(new TextBlock
        {
            Text = meta,
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)FindResource("MutedBrush")
        });

        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Summary,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var open = new Button
        {
            Content = AppLocalization.Text(_settings.Language, "打开", "Open"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Tag = item.Url
        };
        open.Click += OnOpenNoticeClick;
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);

        border.Child = grid;
        return border;
    }

    private void OnOpenNoticeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;
        // Only ever an absolute https address: NoticeFeed refuses anything else when it
        // parses, so a document cannot talk the shell into opening a local path.
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
    }

    private void OnNotifyChanged(object sender, RoutedEventArgs e)
    {
        _settings.NoticesNotify = NotifyBox.IsChecked == true;
        SaveQuietly();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        var original = RefreshButton.Content;
        RefreshButton.Content = AppLocalization.Text(_settings.Language, "获取中…", "Fetching…");
        try
        {
            await NoticeFeed.RefreshAsync(_http);
            // Anything that arrived with this refresh counts as new, since the watermark
            // was recorded before the fetch.
            _unreadAtOpen = NoticeFeed.NewerThan(_settings.NoticesSeenSeq).Count;
            Rebuild();
            MarkSeen();
        }
        finally
        {
            RefreshButton.Content = original;
            RefreshButton.IsEnabled = true;
            _busy = false;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
