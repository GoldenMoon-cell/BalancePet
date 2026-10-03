using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    private readonly ObservableCollection<NotificationSection> _sections = [];
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly BubbleWindow _infoWindow = new();
    private readonly TakeoverPreference _takeover;
    private string _filter = "notice";
    private string _lastFingerprint = "";
    private string _coreVersion = "";
    private string _appliedAppearance = "";
    private NotificationAppearance? _publishedAppearance;

    /// <summary>
    /// How long each row waits before it starts arriving, and how long it takes.
    /// </summary>
    /// <remarks>
    /// A list that appears all at once reads as a table being loaded; rows that set off one
    /// after another read as something arriving, which is what a window belonging to a
    /// desktop pet should feel like. The step is small enough that five rows are done in
    /// well under a second.
    /// </remarks>
    private static readonly TimeSpan RowStep = TimeSpan.FromMilliseconds(80);
    private int _arrivals;
    private DateTime _lastArrival;
    private static readonly Duration RowSlide = new(TimeSpan.FromMilliseconds(340));
    private static readonly Duration RowFade = new(TimeSpan.FromMilliseconds(220));

    public MainWindow()
    {
        _store = new NotificationEventStore(ReadDataDirectory(Environment.GetCommandLineArgs()));
        InitializeComponent();
        ApplySystemTheme();
        _takeover = new TakeoverPreference();
        SectionList.ItemsSource = _sections;
        EventsList.ItemsSource = _rows;
        TakeoverSwitch.IsChecked = _takeover.Enabled;
        LoadPetAvatar();
        _refreshTimer.Tick += (_, _) => Refresh(false);
        Refresh();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
        Closed += (_, _) => _infoWindow.Close();
    }

    /// <summary>
    /// The pet's own artwork, cropped to its head, in the title bar.
    /// </summary>
    /// <remarks>
    /// The window is otherwise a list of text; this is what makes it the pet's window
    /// rather than a generic one. Missing artwork is not an error — the round backing stays.
    /// </remarks>
    private void LoadPetAvatar()
    {
        var path = PreviewRenderer.FindInstalledPetArtwork();
        if (path is null || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            // The head: the artwork is a full figure and a portrait wants the top of it.
            var crop = new CroppedBitmap(image, new Int32Rect(
                (int)(image.PixelWidth * 0.22), 0, (int)(image.PixelWidth * 0.56), (int)(image.PixelHeight * 0.42)));
            PetAvatar.Source = crop;
        }
        catch (Exception)
        {
            // A portrait is decoration; a failure to load one must not stop the window.
        }
    }

    /// <summary>
    /// One row arriving: it slides in from the left and takes its body text a few frames
    /// later, so a row assembles rather than arriving finished.
    /// </summary>
    private void RowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement root || root.DataContext is not NotificationRow row) return;

        // The transform is made here rather than declared in the template: a freezable in a
        // template arrives frozen, and animating a frozen one throws — which is what happened
        // the first time this ran, on every row of the real window. The offset is set first,
        // so a row is already to the left when it starts arriving rather than jumping there.
        var offset = new TranslateTransform(-46, 0);
        root.RenderTransform = offset;

        // Counted per wave of arrivals, not by the row number. The number meant a row two
        // hundred down waited sixteen seconds for its turn, so scrolling showed an empty list:
        // the animated rows were the ones that had already scrolled past.
        var now = DateTime.UtcNow;
        _arrivals = now - _lastArrival > TimeSpan.FromMilliseconds(400) ? 0 : _arrivals + 1;
        _lastArrival = now;
        // Capped so a wave of two hundred rows still finishes inside a second; a scroll that
        // brings a screenful into view is a wave like any other and starts its own.
        var delay = TimeSpan.FromMilliseconds(Math.Min(_arrivals, 12) * RowStep.TotalMilliseconds);
        offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, RowSlide)
        {
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, RowFade) { BeginTime = delay });
    }

    /// <summary>Opens what an entry points at. Only https, and only through the shell.</summary>
    private void RowActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || url.Length == 0) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception)
        {
            // Nothing to do about a shell that will not open a link; the address is on screen.
        }
    }

    private void SectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not NotificationSection section) return;
        if (string.Equals(_filter, section.Key, StringComparison.Ordinal)) return;
        _filter = section.Key;
        Refresh(true);
    }

    /// <summary>
    /// Takes over the pet's bubbles, or gives them back.
    /// </summary>
    /// <remarks>
    /// The host decides whether to show its own bubble by looking for the presenter mutex,
    /// so this switch is only a matter of holding it or letting it go — no message has to be
    /// sent anywhere, and turning it off restores the host's own bubbles immediately.
    /// </remarks>
    private void TakeoverChanged(object sender, RoutedEventArgs e)
    {
        _takeover.Enabled = TakeoverSwitch.IsChecked == true;
        Refresh(true);
    }

    /// <summary>Opens on a named section. Used by the screenshot mode, and by nothing else.</summary>
    internal void ShowSection(string key)
    {
        _filter = key;
        Refresh(true);
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseClick(object sender, RoutedEventArgs e) => Hide();

    private void Refresh(bool force = true)
    {
        var all = _store.ReadAll();
        var liveState = NotificationLiveState.Read(_store.DirectoryPath);
        if (!string.IsNullOrWhiteSpace(liveState?.CoreVersion)) _coreVersion = liveState.CoreVersion;
        if (string.IsNullOrWhiteSpace(_coreVersion)) _coreVersion = ReadCoreVersion();
        // The hover plates keep reading every kind of message: the list is what changed,
        // not what the pet knows.
        _infoWindow.UpdateItems(CreateAroundPetItems(all, _coreVersion, liveState));
        // Following the host's theme, applied when it changes rather than on every tick:
        // replacing ten brushes redraws the window, and this runs twice a second.
        var appearance = liveState?.Appearance;
        var appearancePrint = appearance?.Fingerprint ?? "";
        if (!string.Equals(appearancePrint, _appliedAppearance, StringComparison.Ordinal))
        {
            _appliedAppearance = appearancePrint;
            _publishedAppearance = appearance;
            ApplySystemTheme();
        }

        var fingerprint = all.Count == 0 ? "empty" : $"{all.Count}:{all[0].EventId}:{_filter}";
        if (!force && string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)) return;
        _lastFingerprint = fingerprint;

        RebuildSections(all);
        var filtered = string.Equals(_filter, "all", StringComparison.Ordinal)
            ? all
            : all.Where(value => string.Equals(SectionOf(value.Category), _filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        _rows.Clear();
        // A fresh list is a fresh wave: the rows that fill it stagger from the first one, not
        // from wherever the previous list happened to have got to.
        _arrivals = 0;
        _lastArrival = default;
        for (var index = 0; index < filtered.Count; index++) _rows.Add(new NotificationRow(filtered[index], index));

        var title = SectionName(_filter);
        SectionTitle.Text = title;
        SectionSubtitle.Text = filtered.Count == 0 ? "暂无内容" : $"共 {filtered.Count:N0} 条 · 由桌宠自动获取";
        EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = string.Equals(_filter, "notice", StringComparison.Ordinal)
            ? "还没有更新记录。桌宠每次获取到新内容都会写在这里。"
            : "这一类还没有内容。";
        EventsList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = _takeover.Enabled
            ? "本地保存 · 已接管气泡 · 关闭窗口后继续在后台"
            : "本地保存 · 气泡仍由桌宠显示 · 关闭窗口后继续在后台";
    }

    /// <summary>The sections, with the count each one currently holds.</summary>
    private void RebuildSections(IReadOnlyList<NotificationEvent> all)
    {
        var desired = new (string Key, string Name)[]
        {
            ("notice", "更新记录"), ("task", "任务"), ("account", "账户"),
            ("balance", "余额"), ("system", "系统"), ("all", "全部消息")
        };
        var selected = _filter;
        _sections.Clear();
        foreach (var (key, name) in desired)
        {
            // Counted through the same mapping the list is filtered by, so a section number is
            // always the number of rows it will show — including 全部消息, which no longer
            // counts the interaction records it does not display.
            var count = string.Equals(key, "all", StringComparison.Ordinal)
                ? all.Count(item => SectionOf(item.Category) is not null)
                : all.Count(item => string.Equals(SectionOf(item.Category), key, StringComparison.OrdinalIgnoreCase));
            // 全部消息 is always offered; a section with nothing in it is not, because a
            // navigation rail full of empty rooms is worse than a short one.
            // 更新记录 stays even when empty: it is what this window is for now, and a
            // section that vanishes until the first entry arrives looks like a missing feature.
            if (count == 0 && key is not ("all" or "notice")) continue;
            _sections.Add(new NotificationSection(key, name, count));
        }
        if (_sections.All(section => !string.Equals(section.Key, selected, StringComparison.Ordinal)))
        {
            _filter = _sections.Count > 0 ? _sections[0].Key : "all";
            selected = _filter;
        }
        SectionList.SelectedItem = _sections.FirstOrDefault(section => string.Equals(section.Key, selected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Which section a category belongs to.
    /// </summary>
    /// <remarks>
    /// Changelog entries have a section of their own; everything else is grouped the way the
    /// messages already were. This is the mapping that makes the layout outlive a window
    /// whose only content was the changelog.
    /// </remarks>
    /// <summary>
    /// Which section a category belongs to, or null for one that is not shown here at all.
    /// </summary>
    /// <remarks>
    /// The sections are the categories the host actually writes, taken from a real event
    /// stream rather than invented: notice, task, account, balance, system. Two decisions are
    /// worth stating because neither is visible in the result.
    ///
    /// `interaction` is excluded. It is the largest category by far — 982 of 1863 records on
    /// the machine this was written on — and it is a record of clicks and drags, which is
    /// telemetry rather than something a person reads. It stays in the stream, where the hover
    /// information takes its values from; it just is not offered as a room to walk into.
    ///
    /// `refresh` and anything unknown fold into 系统 rather than being dropped. A category that
    /// silently disappears when a future host starts writing it is worse than one filed in the
    /// wrong drawer, and the alternative — a section that appears and vanishes with the data —
    /// makes the navigation move under the reader's hand.
    /// </remarks>
    private static string? SectionOf(string category) => category.ToLowerInvariant() switch
    {
        "notice" => "notice",
        "task" => "task",
        "account" => "account",
        "balance" => "balance",
        "interaction" => null,
        _ => "system"
    };

    private static string SectionName(string key) => key switch
    {
        "notice" => "更新记录",
        "task" => "任务",
        "account" => "账户",
        "balance" => "余额",
        "system" => "系统",
        _ => "全部消息"
    };


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
            items.Add(new NotificationBubble($"当前登录方式 · {mode}", detail, "account", mode));
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
                items.Add(new NotificationBubble($"当前登录方式 · {(official ? "官方登录" : "CC Switch")}", detail, "account", official ? "官方登录" : "CC Switch"));
            }
        }

        if (liveState?.TaskKnown == true)
        {
            var provider = string.IsNullOrWhiteSpace(liveState.TaskProvider) ? "AI 任务" : liveState.TaskProvider;
            var detail = liveState.TaskActive
                ? liveState.TaskCount > 1 ? $"{liveState.TaskCount} 个任务" : "正在处理"
                : "当前没有正在处理的任务";
            items.Add(new NotificationBubble($"{provider} {(liveState.TaskActive ? "工作中" : "已停止")}", detail, "task", liveState.TaskActive ? "工作中" : "已停止"));
        }
        else
        {
            var task = all.FirstOrDefault(item =>
                item.Title.Contains("工作中", StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains("已停止", StringComparison.OrdinalIgnoreCase));
            if (task is not null) items.Add(ToBubble(task, "task"));
        }

        if (!string.IsNullOrWhiteSpace(coreVersion))
            items.Add(new NotificationBubble($"当前版本 · {coreVersion}", "BalancePet", "system", $"v{coreVersion}"));
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
            : new NotificationBubble(primary, detail, "balance", current?.Amount ?? "");
    }

    private static NotificationBubble? CreateLiveBalanceBubble(NotificationLiveState state)
    {
        if (!state.HasBalance) return null;
        var currency = string.IsNullOrWhiteSpace(state.Currency) ? "USD" : state.Currency;
        var primary = $"余额 {state.Balance!.Value:0.00} {currency}";
        var detail = state.HasSpent
            ? $"本次消耗 {(state.Spent!.Value > 0 ? "-" : "")}{Math.Abs(state.Spent.Value):0.00} {(string.IsNullOrWhiteSpace(state.SpentCurrency) ? currency : state.SpentCurrency)}"
            : "等待下一次余额刷新";
        return new NotificationBubble(primary, detail, "balance", state.Balance is { } value ? $"{value:0.00}" : "");
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
        // A palette from the host wins over reading the system: the user chose a theme in the
        // host, and to them this window is part of the same program.
        if (_publishedAppearance is not null && ApplyPublishedAppearance(resources, _publishedAppearance)) return;
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

    /// <summary>
    /// Wears the host's colours and face.
    /// </summary>
    /// <remarks>
    /// The same resource keys the host fills, with the values it reported, so the two windows
    /// look like one program. A colour the host did not send is left alone rather than
    /// defaulted — this extension's own value is a better answer than black — and a font it
    /// named but which cannot be resolved is skipped rather than fatal.
    ///
    /// Returns false when the host sent nothing usable, which leaves the caller free to fall
    /// back to reading the system.
    /// </remarks>
    private static bool ApplyPublishedAppearance(ResourceDictionary resources, NotificationAppearance appearance)
    {
        var applied = false;
        applied |= SetIfPresent(resources, "WindowBrush", appearance.Window);
        applied |= SetIfPresent(resources, "TitleBarBrush", appearance.Sidebar);
        applied |= SetIfPresent(resources, "PanelBrush", appearance.Sidebar);
        applied |= SetIfPresent(resources, "CardBrush", appearance.Surface);
        applied |= SetIfPresent(resources, "ControlHoverBrush", appearance.Control);
        applied |= SetIfPresent(resources, "BorderBrush", appearance.Border);
        applied |= SetIfPresent(resources, "TextBrush", appearance.Text);
        applied |= SetIfPresent(resources, "MutedBrush", appearance.Muted);
        applied |= SetIfPresent(resources, "AccentBrush", appearance.Accent);
        applied |= SetIfPresent(resources, "AccentSoftBrush", appearance.AccentSoft);

        if (!string.IsNullOrWhiteSpace(appearance.Font))
        {
            try
            {
                resources["UiFontFamily"] = new FontFamily(appearance.Font);
                applied = true;
            }
            catch (Exception error) when (error is ArgumentException or UriFormatException)
            {
            }
        }
        return applied;
    }

    private static bool SetIfPresent(ResourceDictionary resources, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
            {
                resources[key] = new SolidColorBrush(color);
                return true;
            }
        }
        catch (FormatException) { }
        return false;
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color) => resources[key] = new SolidColorBrush(color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>One section of the rail.</summary>
    private sealed class NotificationSection(string key, string name, int count)
    {
        public string Key { get; } = key;
        public string Name { get; } = name;
        public string Count { get; } = count.ToString("N0");
        public Visibility BadgeVisibility => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Whether this extension is presenting the pet's bubbles, remembered between runs.
    /// </summary>
    /// <remarks>
    /// Stored beside the extension rather than in the host's settings: it decides what the
    /// extension does, and the host's own answer to "should I bubble" is unchanged — it
    /// keeps looking for the presenter marker, which is what this setting holds or releases.
    /// </remarks>
    private sealed class TakeoverPreference
    {
        private readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BalancePet", "notification-center.json");

        public TakeoverPreference()
        {
            try
            {
                if (!File.Exists(_path)) return;
                using var document = JsonDocument.Parse(File.ReadAllText(_path));
                if (document.RootElement.TryGetProperty("takeover", out var value)
                    && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    Enabled = value.GetBoolean();
            }
            catch (Exception)
            {
                // An unreadable preference means the default, which is to take over.
            }
        }

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                Save();
            }
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_path, JsonSerializer.Serialize(new { takeover = _enabled }));
                PresenterMarker.Apply(_enabled);
            }
            catch (Exception)
            {
                // Remembering the choice matters less than acting on it.
                PresenterMarker.Apply(_enabled);
            }
        }
    }

    /// <summary>The row as the list sees it.</summary>
    private sealed class NotificationRow
    {
        private readonly NotificationEvent _event;
        public NotificationRow(NotificationEvent value, int index)
        {
            _event = value;
            Index = index;
        }

        /// <summary>Position in the list, which is what staggers the arrival.</summary>
        public int Index { get; }
        public string Title => string.IsNullOrWhiteSpace(_event.Title) ? "消息" : _event.Title;
        public string Amount => _event.Amount;
        public string Detail => _event.Detail;
        public string Url => _event.Url ?? "";
        // Falls back to the category itself, which cannot happen while categories that map to
        // nothing are also kept out of every list — and if it ever does, showing the raw name
        // is a better failure than wearing someone else's label.
        public string CategoryText => MainWindow.SectionOf(_event.Category) is { } section
            ? MainWindow.SectionName(section)
            : _event.Category;
        public string OccurredAtText => _event.OccurredAt.ToLocalTime().ToString("MM-dd HH:mm");
        public Brush CategoryBrush => _event.Category switch
        {
            "balance" or "notice" => Brush("#078C82"),
            "refresh" => Brush("#344F91"),
            "task" => Brush("#8A5CC7"),
            "account" => Brush("#D38A1A"),
            "system" => Brush("#71809D"),
            _ => Brush("#C43B52")
        };
        public Brush CategorySoftBrush => _event.Category switch
        {
            "balance" or "notice" => Brush("#1A078C82"),
            "refresh" => Brush("#1A344F91"),
            "task" => Brush("#1A8A5CC7"),
            "account" => Brush("#1AD38A1A"),
            "system" => Brush("#1A71809D"),
            _ => Brush("#1AC43B52")
        };
        public Visibility ActionVisibility => Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

        private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    }
}
