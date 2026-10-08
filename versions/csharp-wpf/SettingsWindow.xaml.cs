using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BalancePet.Wpf.Models;
using BalancePet.Wpf.Services;

namespace BalancePet.Wpf;

public partial class SettingsWindow : Window
{
    public bool SettingsApplied { get; private set; }
    public event EventHandler? SettingsAppliedChanged;

    private readonly SettingsStore _store;
    private readonly DpapiTokenStore _tokens;
    private readonly PetSettings _settings;
    private readonly BrowserSessionBridgeServer? _browserSessionBridge;
    private readonly PetExtensionManager _extensions = new();
    private readonly FeatureExtensionManager _featureExtensions;
    private readonly ThemeExtensionManager _themes = new();
    private readonly ExtensionPackageCatalog _extensionLibrary = new();
    /// <summary>
    /// One client for the whole process, not one per settings window.
    /// </summary>
    /// <remarks>
    /// It was per window, and closing a window disposed it -- which cancelled a download that was
    /// still running, so the disposal was removed. That left the other half of the problem: every
    /// open leaked a client and its connections, and after enough of them new requests stopped
    /// being able to connect at all, which is what "opening the plugin catalogue just sits there"
    /// was. Static is what HttpClient is meant to be: shared, long-lived, and never disposed.
    /// </remarks>
    private static readonly HttpClient _extensionUpdateHttpClient = Networking.CreateClient(TimeSpan.FromSeconds(20));
    private readonly ExtensionUpdateService _extensionUpdates;
    private readonly PluginCatalogService _pluginCatalog;
    private readonly CancellationTokenSource _pluginCatalogCancellation = new();
    private IReadOnlyList<PluginCatalogRecord> _pluginCatalogEntries = Array.Empty<PluginCatalogRecord>();
    private readonly List<MonitorProfile> _profiles;
    private string _currentProfileId = "";
    private bool _suppressProfileChange;
    private bool _suppressRefreshChange;
    private bool _suppressPresetChange;
    private bool _suppressSiteUrlChange;
    private bool _suppressLanguageChange;
    private bool _suppressThemeChange;
    private bool _trackChanges;
    private bool _suppressChangeTracking;
    private bool _hasUnsavedChanges;
    private bool _allowCloseWithoutPrompt;
    private bool _navigationCollapsed;
    private int _navigationTransitionId;
    private string _selectedThemeMode = "system";
    private string _selectedThemeBackdrop = "mica";
    private string _selectedThemeId = ThemeExtensionManager.BundledThemeId;
    private const double ExpandedNavigationWidth = 178;
    private const double CollapsedNavigationWidth = 58;
    private static readonly TimeSpan NavigationAnimationDuration = TimeSpan.FromMilliseconds(220);

    public SettingsWindow(SettingsStore store, DpapiTokenStore tokens, PetSettings settings, FeatureExtensionManager? featureExtensions = null, BrowserSessionBridgeServer? browserSessionBridge = null)
    {
        InitializeComponent();

        // Counted when the page appears rather than when the button is pressed, so the row reads
        // as a fact about the disk instead of as a result of having clicked something.
        Loaded += (_, _) => UpdateIconCacheText(); _store = store; _tokens = tokens; _settings = settings; _featureExtensions = featureExtensions ?? new FeatureExtensionManager(); _browserSessionBridge = browserSessionBridge;
        if (_browserSessionBridge is not null) _browserSessionBridge.SessionReceived += OnBrowserSessionReceived;
        _themes.EnsureBundledThemeInstalled();
        _extensionUpdates = new ExtensionUpdateService(_extensionUpdateHttpClient);
        _pluginCatalog = new PluginCatalogService(_extensionUpdateHttpClient);
        // The HTTP client is deliberately not disposed here. Closing the panel used to dispose it, and
// an extension install that was already under way was riding on it: the transfer died with the
// window, and the only way to finish was to open the panel and press the button again. The
// download is not the window's work -- the window shows it.
        PackageInstallsChanged += OnPackageInstallsChanged;
        StartStatusFade();
        PackageProgressChanged += OnPackageProgressChanged;
        Closed += (_, _) =>
        {
            PackageInstallsChanged -= OnPackageInstallsChanged;
            PackageProgressChanged -= OnPackageProgressChanged;
            if (_browserSessionBridge is not null) _browserSessionBridge.SessionReceived -= OnBrowserSessionReceived;
            _pluginCatalogCancellation.Cancel();
            _pluginCatalogCancellation.Dispose();
        };
        Closing += OnWindowClosing;
        AddInstalledPetStyles();
        RefreshExtensionList();
        UpdatePetStyleAvailability();
        RefreshThemeList(settings.ThemeId);
        SelectByTag(ThemeModeBox, settings.ThemeMode);
        SelectByTag(ThemeBackdropBox, settings.ThemeBackdrop);
        _selectedThemeMode = SelectedTag(ThemeModeBox, "system");
        _selectedThemeBackdrop = SelectedTag(ThemeBackdropBox, "mica");
        _selectedThemeId = SelectedTag(ThemeBox, ThemeExtensionManager.BundledThemeId);
        Diagnostics.Banner(typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "?");
        Diagnostics.Write("start", "设置窗口已构造");
        AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnAnyBringIntoView), true);
        _selectedUiFont = settings.UiFont ?? "";
        PopulateUiFonts();
        ApplySelectedTheme();
        _profiles = settings.Monitors is { Count: > 0 }
            ? settings.Monitors.Select(CloneProfile).ToList()
            : new List<MonitorProfile> { CreateProfileFromLegacy(settings) };
        RefreshProfileList(settings.SelectedMonitorId);
        SelectByTag(PetStyleBox, settings.PetStyle); SelectByTag(InteractionBox, settings.InteractionMode); SelectByTag(UpdateCheckBox, settings.UpdateCheckMode); SelectByTag(ExtensionUpdateCheckBox, settings.ExtensionUpdateCheckMode);
        _suppressLanguageChange = true;
        SelectByTag(LanguageBox, settings.Language);
        _suppressLanguageChange = false;
 ScaleSlider.Value = Math.Clamp(settings.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(settings.Volume, 0, 1); SoundBox.IsChecked = settings.Sound; BubbleBox.IsChecked = settings.Bubble; NoticeBubbleBox.IsChecked = settings.NoticesNotify; InteractionEffectsBox.IsChecked = settings.InteractionEffects; NavigationAnimationsBox.IsChecked = settings.NavigationAnimations; EasterEggsBox.IsChecked = settings.RandomEasterEggs; FollowCodexBox.IsChecked = settings.CodexTaskIntegration; FollowDeepSeekHarnessBox.IsChecked = settings.DeepSeekHarnessIntegration; FollowGeminiBox.IsChecked = settings.GeminiTaskIntegration; FollowQwenBox.IsChecked = settings.QwenTaskIntegration; FollowClaudeBox.IsChecked = settings.ClaudeTaskIntegration; FollowOtherBox.IsChecked = settings.OtherTaskIntegration; StartupBox.IsChecked = settings.StartWithWindows || StartupManager.IsEnabled();
        _navigationCollapsed = settings.NavigationCollapsed;
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        AppLocalization.Apply(this, settings.Language);
        RefreshLanguageSelector(settings.Language, selectLanguage: false);
        SyncAllComboDisplays();
        UpdatePetStyleAvailability();
        UpdateClientAvailability();
        UpdateAccountSummary();
        ApplyNavigationState();
        _trackChanges = true;
        // The one preview build that counts: earlier requests were refused while the
        // window was still being assembled, and the artwork itself is decoded off the
        // UI thread so the window can open without waiting for nine PNGs.
        RefreshPetPreview();
    }

    private void OnToggleNavigation(object sender, RoutedEventArgs e)
    {
        _navigationCollapsed = !_navigationCollapsed;
        PersistNavigationPreference();
        ApplyNavigationState(animate: NavigationAnimationsBox.IsChecked == true);
    }

    private void PersistNavigationPreference()
    {
        _settings.NavigationCollapsed = _navigationCollapsed;
        try
        {
            _store.Save(_settings);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ApplyNavigationState(bool animate = false)
    {
        if (SettingsTabs is null || NavigationToggleButton is null) return;
        var textBlocks = new[]
        {
            AccountNavigationText, PetNavigationText, AiNavigationText, ExtensionNavigationText,
            AppearanceNavigationText, AdvancedNavigationText
        };
        var transitionId = ++_navigationTransitionId;

        if (!animate || !IsLoaded)
        {
            StopNavigationAnimationsAtCurrentValues(textBlocks);
            SettingsTabs.Tag = _navigationCollapsed ? "collapsed" : "expanded";
            SetNavigationLabels(textBlocks, visible: !_navigationCollapsed, opacity: _navigationCollapsed ? 0 : 1);
            SetNavigationSidebarWidth(_navigationCollapsed ? CollapsedNavigationWidth : ExpandedNavigationWidth, animate: false);
        }
        else if (_navigationCollapsed)
        {
            StopNavigationAnimationsAtCurrentValues(textBlocks);
            AnimateNavigationLabels(textBlocks, visible: false, (_, _) =>
            {
                if (transitionId != _navigationTransitionId || !_navigationCollapsed) return;
                SetNavigationLabels(textBlocks, visible: false, opacity: 0);
                SettingsTabs.Tag = "collapsed";
                SetNavigationSidebarWidth(CollapsedNavigationWidth, animate: true);
            });
        }
        else
        {
            StopNavigationAnimationsAtCurrentValues(textBlocks);
            SetNavigationLabels(textBlocks, visible: false, opacity: 0);
            SettingsTabs.Tag = "collapsed";
            SetNavigationSidebarWidth(ExpandedNavigationWidth, animate: true, (_, _) =>
            {
                if (transitionId != _navigationTransitionId || _navigationCollapsed) return;
                SettingsTabs.Tag = "expanded";
                SetNavigationLabels(textBlocks, visible: true, opacity: 0);
                AnimateNavigationLabels(textBlocks, visible: true, completed: null);
            });
        }
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var tooltip = _navigationCollapsed
            ? AppLocalization.Text(language, "展开导航栏", "Expand navigation pane")
            : AppLocalization.Text(language, "折叠导航栏", "Collapse navigation pane");
        NavigationToggleButton.ToolTip = tooltip;
        System.Windows.Automation.AutomationProperties.SetName(NavigationToggleButton, tooltip);
    }

    private static void SetNavigationLabels(IReadOnlyList<TextBlock> textBlocks, bool visible, double opacity)
    {
        foreach (var text in textBlocks)
        {
            text.BeginAnimation(UIElement.OpacityProperty, null);
            text.Opacity = opacity;
            text.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void AnimateNavigationLabels(IReadOnlyList<TextBlock> textBlocks, bool visible, EventHandler? completed)
    {
        for (var index = 0; index < textBlocks.Count; index++)
        {
            var text = textBlocks[index];
            if (visible) text.Visibility = Visibility.Visible;
            var animation = new DoubleAnimation
            {
                To = visible ? 1 : 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            if (index == 0 && completed is not null) animation.Completed += completed;
            text.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }

    private void StopNavigationAnimationsAtCurrentValues(IReadOnlyList<TextBlock> textBlocks)
    {
        var sidebarWidth = NavigationSidebar.ActualWidth;
        if (sidebarWidth > 0)
        {
            NavigationSidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
            NavigationSidebar.Width = sidebarWidth;
        }

        foreach (var text in textBlocks)
        {
            var opacity = text.Opacity;
            text.BeginAnimation(UIElement.OpacityProperty, null);
            text.Opacity = opacity;
        }
    }

    private void SetNavigationSidebarWidth(double targetWidth, bool animate, EventHandler? completed = null)
    {
        if (NavigationSidebar is null) return;
        if (!animate)
        {
            NavigationSidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
            NavigationSidebar.Width = targetWidth;
            return;
        }

        var from = NavigationSidebar.ActualWidth > 0 ? NavigationSidebar.ActualWidth : NavigationSidebar.Width;
        var animation = new DoubleAnimation
        {
            From = from,
            To = targetWidth,
            Duration = new Duration(NavigationAnimationDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        if (completed is not null) animation.Completed += completed;
        NavigationSidebar.BeginAnimation(FrameworkElement.WidthProperty, animation);
    }

    private void RefreshLanguageSelector(string language, bool selectLanguage)
    {
        if (LanguageBox is null) return;
        _suppressLanguageChange = true;
        try
        {
            foreach (var item in LanguageBox.Items.OfType<ComboBoxItem>())
            {
                item.Content = item.Tag?.ToString() switch
                {
                    "zh-CN" => AppLocalization.Text(language, "简体中文", "Simplified Chinese"),
                    "en-US" => "English",
                    _ => item.Content
                };
            }
            if (selectLanguage) SelectByTag(LanguageBox, language);
        }
        finally { _suppressLanguageChange = false; }
    }

    /// <summary>
    /// Fills the appearance selector with what this installation can actually draw.
    /// </summary>
    /// <remarks>
    /// Rebuilt rather than topped up, and from the catalogue rather than from a list in
    /// the XAML. Appearances arrive as packages now, so a written-out list offers every
    /// appearance ever published -- almost all of them disabled -- on an installation
    /// that has none of them, which is exactly the state a fresh installation starts in.
    ///
    /// The name comes from the catalogue or the installed package's manifest, so a
    /// package the program has never heard of still shows the name its author chose.
    /// </remarks>
    private void AddInstalledPetStyles()
    {
        if (PetStyleBox is null) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var english = AppLocalization.IsEnglish(language);
        var previous = SelectedTag(PetStyleBox, "");
        PetStyleBox.Items.Clear();
        foreach (var definition in PetStyleCatalog.GetAvailableStyles())
        {
            var installed = PetStyleCatalog.GetExtensionStyleId(definition.Id);
            PetStyleBox.Items.Add(new ComboBoxItem
            {
                Tag = definition.Id,
                Content = english ? definition.EnglishName : definition.ChineseName,
                ToolTip = string.IsNullOrWhiteSpace(installed)
                    ? AppLocalization.Text(language, "随主程序提供的形象", "Ships with the program")
                    : $"{AppLocalization.Text(language, "形象包：", "Appearance package: ")}{installed}"
            });
        }
        // Restoring the selection keeps the panel showing the same appearance across an
        // install or uninstall, which is when this runs.
        if (previous.Length > 0) SelectByTag(PetStyleBox, previous);
        RefreshPetPreview();
    }

    /// <summary>
    /// Moves the selection off an appearance that is no longer installed.
    /// </summary>
    /// <remarks>
    /// Any installed appearance will do; there is no default one any more, and the
    /// built-in placeholder guarantees the list is never empty.
    /// </remarks>
    private void SelectFirstAvailablePetStyle()
    {
        var next = PetStyleCatalog.GetAvailableStyles().FirstOrDefault();
        SelectByTag(PetStyleBox, next?.Id ?? PetStyleCatalog.FallbackId);
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox box, string tag)
    {
        var requested = string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim();
        foreach (ComboBoxItem item in box.Items)
        {
            if (!item.IsEnabled || !string.Equals(item.Tag?.ToString(), requested, StringComparison.OrdinalIgnoreCase)) continue;
            box.SelectedItem = item;
            return;
        }
        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    private static string SelectedTag(System.Windows.Controls.ComboBox box, string fallback)
    {
        var tag = (box.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(tag)) return tag;
        var value = box.SelectedValue as string;
        return !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static void SyncComboDisplay(System.Windows.Controls.ComboBox? box, string? explicitText = null)
    {
        if (box is null) return;
        void Update()
        {
            box.ApplyTemplate();
            if (box.Template.FindName("SelectionText", box) is TextBlock text)
                text.Text = explicitText ?? (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            box.InvalidateVisual();
        }
        Update();
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, Update);
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, Update);
        // Loaded, not Render. Changing the font resource re-templates every control, and a
        // template is applied during layout, which runs after Render priority. Filling the
        // text in before that means filling in a TextBlock that is about to be thrown away,
        // which is why the box went blank for every face except the one already in use.
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Update);
    }

    private void SyncAllComboDisplays()
    {
        SyncComboDisplay(ProfileBox);
        SyncComboDisplay(PresetBox);
        SyncComboDisplay(AuthModeBox);
        SyncComboDisplay(RefreshBox);
        SyncComboDisplay(PetStyleBox);
        SyncComboDisplay(InteractionBox);
        SyncComboDisplay(UpdateCheckBox);
        SyncComboDisplay(ExtensionUpdateCheckBox);
        SyncComboDisplay(LanguageBox);
        SyncComboDisplay(ThemeBox);
        SyncComboDisplay(ThemeModeBox);
        SyncComboDisplay(ThemeBackdropBox);
        SyncComboDisplay(BrowserSessionBrowserBox);
    }

    private void RefreshThemeList(string? selectedId = null)
    {
        if (ThemeBox is null) return;
        var requested = string.IsNullOrWhiteSpace(selectedId) ? SelectedTag(ThemeBox, _settings.ThemeId) : selectedId;
        _suppressThemeChange = true;
        try
        {
            ThemeBox.Items.Clear();
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            foreach (var theme in _themes.GetLatestEnabled())
            {
                ThemeBox.Items.Add(new ComboBoxItem
                {
                    Tag = theme.Manifest.Id,
                    Content = AppLocalization.IsEnglish(language) && !string.IsNullOrWhiteSpace(theme.Manifest.NameEn)
                        ? theme.Manifest.NameEn
                        : theme.Manifest.Name
                });
            }
            SelectByTag(ThemeBox, requested ?? ThemeExtensionManager.BundledThemeId);
            if (ThemeBox.SelectedItem is null && ThemeBox.Items.Count > 0) ThemeBox.SelectedIndex = 0;
            _selectedThemeId = SelectedTag(ThemeBox, requested ?? ThemeExtensionManager.BundledThemeId);
        }
        finally { _suppressThemeChange = false; }
        UpdateThemeSummary();
    }

    private ThemeExtensionInfo? SelectedTheme()
    {
        var id = _selectedThemeId;
        return _themes.GetLatestEnabled(id) ?? _themes.GetLatestEnabled(ThemeExtensionManager.BundledThemeId) ?? _themes.GetLatestEnabled().FirstOrDefault();
    }

    /// <summary>One entry in the interface-font list.</summary>
    /// <remarks>
    /// Preview is a live FontFamily so each row can be drawn in the face it names. A list of
    /// font names all rendered in the same font tells the reader nothing about any of them,
    /// which is the entire reason someone opens this list.
    /// </remarks>
    public sealed record FontChoice(string Label, string Family, System.Windows.Media.FontFamily Preview)
    {
        // The combo template shows SelectionBoxItem, which is the item object when the list is
        // bound to data. This is what turns that object back into the name on the box.
        public override string ToString() => Label;
    }

    private string _selectedUiFont = "";
    private bool _fillingFonts;

    /// <summary>
    /// Fills the font list: the embedded face first, then everything the machine has.
    /// </summary>
    /// <remarks>
    /// Reads the installed families every time rather than caching them, because a font can be
    /// installed or removed while the program is running and a stale list would offer a face
    /// that no longer exists.
    /// </remarks>
    private void PopulateUiFonts()
    {
        if (UiFontBox is null) return;
        _fillingFonts = true;
        try
        {
            // No face is shipped, so the first entry means "leave it to Windows" rather than
            // "the one we chose for you". Nothing is bundled precisely because the list below
            // exists: picking a default on the user's behalf was the thing being avoided.
            var followSystem = new FontChoice(
                AppLocalization.Text(_settings.Language, "跟随系统", "Use system setting"),
                "",
                new System.Windows.Media.FontFamily(WindowThemeService.DefaultFontFamily));

            var names = new SortedSet<string>(StringComparer.CurrentCulture);
            foreach (var family in System.Windows.Media.Fonts.SystemFontFamilies)
            {
                // One family at a time, and a family that cannot be read is skipped rather
                // than fatal. The collection is system state: a font another program
                // registered and then removed leaves an entry whose file is gone, and
                // reading that entry throws from inside WPF's font layer. That is not a
                // reason for opening the settings window to end the program, which is what
                // it did -- measured, with the event log naming this method.
                try
                {
                    if (IsSymbolOnly(family)) continue;
                    var name = PreferredName(family);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
                catch (Exception)
                {
                    // Same rule as TryReadFaces: a family that cannot be read is one that
                    // cannot be offered, whatever the reason and whichever call found out.
                }
            }

            var choices = new List<FontChoice> { followSystem };
            choices.AddRange(names.Select(name => new FontChoice(
                name, name, new System.Windows.Media.FontFamily(name))));

            UiFontBox.ItemsSource = choices;
            var selected = choices.FirstOrDefault(choice =>
                string.Equals(choice.Family, _selectedUiFont, StringComparison.OrdinalIgnoreCase)) ?? followSystem;
            UiFontBox.SelectedItem = selected;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => DumpDiagnostics("列表填充之后")));
        }
        finally { _fillingFonts = false; }
    }

    /// <summary>
    /// Whether a family is a symbol font, and so offers nothing to write an interface in.
    /// </summary>
    /// <remarks>
    /// Wingdings and its relatives map ordinary letters onto pictures rather than leaving them
    /// alone, so choosing one turns the whole window -- including the list this choice is made
    /// in -- into glyphs, with no readable way back. They are left out rather than offered and
    /// regretted. Icon fonts are not excluded: they simply lack the letters, so the ordinary
    /// fallback covers them and the window stays legible.
    /// </remarks>
    private static bool IsSymbolOnly(System.Windows.Media.FontFamily family)
    {
        if (!TryReadFaces(family, out var faces)) return true;
        return faces.Count > 0 && faces.All(face => face.TryGetGlyphTypeface(out var glyphs) && glyphs.Symbol);
    }

    /// <summary>
    /// Reads a family's faces, saying so when it cannot instead of throwing.
    /// </summary>
    /// <remarks>
    /// Every failure means the same thing here: this family cannot be offered, because it
    /// cannot be read. That is why nothing is enumerated. An earlier version listed the
    /// exceptions it expected — <see cref="ArgumentException"/>,
    /// <see cref="NotSupportedException"/>, <see cref="System.IO.FileFormatException"/> —
    /// and the one that arrived was a plain <see cref="System.IO.FileNotFoundException"/>
    /// from the native font layer, for an installed family whose file is no longer there.
    /// The exception left this method, left the window's constructor, and ended the
    /// process: opening the settings window is what crashed, which is what a user
    /// reported. A list of expected failures goes stale exactly the way the project file's
    /// list of appearance names did; a rule does not.
    ///
    /// The body is two library calls and nothing of ours, so there is no bug of our own
    /// for a broad catch to hide. Fatal exceptions are not caught this way in any case:
    /// a stack overflow cannot be.
    /// </remarks>
    internal static bool TryReadFaces(System.Windows.Media.FontFamily family, out List<System.Windows.Media.Typeface> faces)
        => TryRead(() => family.GetTypefaces(), out faces);

    /// <summary>
    /// The guard itself, separated so its contract can be tested without a broken font.
    /// </summary>
    /// <remarks>
    /// Testing it through a font turned out not to be possible. A family naming a file that
    /// does not exist reads back as no faces rather than as a throw, and a hand-written
    /// registry entry pointing at a missing file is skipped by the font stack instead of
    /// being surfaced — so the state that killed the process cannot be conjured up on
    /// demand, and a test built on one would be testing the font stack.
    ///
    /// What can be pinned is the promise: whatever reading throws, the answer is "cannot be
    /// offered". That is the thing a later edit could quietly undo, by narrowing the catch
    /// back to a list of expected exceptions — which is exactly what it was, and the one
    /// that arrived was not on it.
    /// </remarks>
    internal static bool TryRead(Func<IEnumerable<System.Windows.Media.Typeface>> read, out List<System.Windows.Media.Typeface> faces)
    {
        try
        {
            faces = read().ToList();
            return true;
        }
        catch (Exception)
        {
            faces = [];
            return false;
        }
    }

    /// <summary>
    /// The name to show for an installed family: the one matching the interface language when
    /// the font carries it, otherwise the font's own name.
    /// </summary>
    private static string PreferredName(System.Windows.Media.FontFamily family)
    {
        foreach (var tag in new[] { "zh-Hans", "zh-CN", "zh" })
        {
            if (family.FamilyNames.TryGetValue(
                    System.Windows.Markup.XmlLanguage.GetLanguage(tag), out var localized)
                && !string.IsNullOrWhiteSpace(localized)) return localized;
        }
        return family.Source;
    }

    /// <summary>
    /// Reports whatever scrollbars a combo's dropdown has, at the only moment they exist.
    /// </summary>
    /// <remarks>
    /// The earlier dump ran with the dropdown shut and therefore only ever saw the window's own
    /// scrollbar, which measured clean and told us nothing about the one being reported.
    /// </remarks>
    private void DumpOpenDropdown(string label, System.Windows.Controls.ComboBox? box)
    {
        if (box?.Template?.FindName("PART_Popup", box) is not System.Windows.Controls.Primitives.Popup popup
            || popup.Child is not FrameworkElement chrome) return;
        Diagnostics.Thumbs($"{label}（下拉打开时）", chrome);
        Diagnostics.Write("thumb", $"{label}: 弹出层 Render 尺寸={chrome.RenderSize} " +
                                   $"布局裁剪={System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(chrome)} " +
                                   $"Clip={chrome.Clip} 允许透明={popup.AllowsTransparency} " +
                                   $"实际占位={System.Windows.Media.VisualTreeHelper.GetDescendantBounds(chrome)}");
        if (chrome is System.Windows.Controls.Border chromeBorder)
            Diagnostics.Write("thumb", $"{label}: 弹出层 CornerRadius={chromeBorder.CornerRadius} Padding={chromeBorder.Padding}");
    }

    private void OnComboDroppedDown(object sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, UiFontBox)) return;
        // Deferred. The popup's tree does not exist yet when this fires, which is why the
        // first attempt reported no scrollbars at all rather than reporting the wrong ones.
        // The popup is its own visual tree in its own window, so a routed event raised
        // inside it never reaches the settings window and the handler installed there never
        // saw these. Attaching to the popup itself is the only way to catch them: 489 went
        // through unhandled while the window-level handler reported almost none.
        if (UiFontBox.Template?.FindName("PART_Popup", UiFontBox) is System.Windows.Controls.Primitives.Popup popup
            && popup.Child is UIElement chrome)
        {
            chrome.RemoveHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnAnyBringIntoView));
            chrome.AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnAnyBringIntoView), true);
            Diagnostics.Write("bring", "已在弹出层上挂上拦截");
        }
        foreach (var priority in new[]
                 {
                     System.Windows.Threading.DispatcherPriority.Loaded,
                     System.Windows.Threading.DispatcherPriority.ContextIdle
                 })
        {
            Dispatcher.BeginInvoke(priority, new Action(() => DumpOpenDropdown("界面字体", UiFontBox)));
        }
    }

    private void OnUiFontChanged(object sender, SelectionChangedEventArgs e)
    {
        var choice = UiFontBox?.SelectedItem as FontChoice;
        if (!_fillingFonts && _trackChanges)
        {
            _selectedUiFont = choice?.Family ?? "";
            // Applied before the display is filled in, not after. Swapping the font resource
            // re-templates every control in the window, which builds a fresh, empty selection
            // TextBlock; filling it first means the new one is the blank that gets seen.
            WindowThemeService.ApplyFont(this, _selectedUiFont);
            MarkSettingsDirty();
            Diagnostics.Write("pick", $"选了 ui_font=<{_selectedUiFont}>");
            DumpDiagnostics("选完之后立刻");
            // Again after layout: the fault only appeared once the re-template had run.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => DumpDiagnostics("布局之后")));
        }
        // Deliberately not SyncedComboDisplay'd. That helper writes Text directly, a local
        // value beats a binding, and it is the local value that a re-template throws away --
        // which is exactly what left this box blank after a face was picked. The binding in
        // the template now resolves for data items, so there is nothing to fill in.
        _ = choice;
    }

    /// <summary>
    /// Records every request to scroll something into view, wherever it comes from.
    /// </summary>
    /// <remarks>
    /// Hung on the window rather than on one list, because the scrolling row was reported on
    /// every list and singling one out would have hidden the others.
    /// </remarks>
    private void OnAnyBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        Diagnostics.BringIntoView("窗口", sender, e);

        // The pointer resting on the last, half-cut row of a list scrolled the list by several
        // rows. The stack says why: hovering moves keyboard focus to that row, and focus asks
        // for the row to be brought into view, which WPF satisfies by scrolling it fully into
        // sight. The row was already almost entirely visible, so the scroll is pure loss.
        //
        // A request is allowed through when the target is genuinely out of view, which is what
        // keeps keyboard navigation working: arrowing past the edge still scrolls.
        // Nothing inside a list is scrolled on request any more. The lists have their own
        // scrollbars and the wheel works; a request to reveal a row arrives whenever the
        // pointer moves over one, because hovering moves keyboard focus, and honouring it is
        // what made the list jump by rows. Forty-five of these still went through under the
        // previous rule, which asked only whether the row was out of sight.

    }

    /// <summary>Whether an element belongs to a list or a combo dropdown rather than a page.</summary>
    private static bool IsInsideList(DependencyObject target)
    {
        for (var node = target; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.Primitives.Selector)
                return true;
            // No early exit on ScrollViewer. The requests that were being allowed all had a
            // ScrollViewer as their target, so treating one as proof of "not a list" excluded
            // exactly the cases that mattered.
        }
        return false;
    }

    /// <summary>Whether an element is already far enough inside its viewport to be readable.</summary>
    private static bool IsMostlyInView(FrameworkElement element)
    {
        DependencyObject? node = element;
        while (node is not null and not ScrollViewer)
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        if (node is not ScrollViewer viewport || viewport.ViewportHeight <= 0) return false;

        try
        {
            var top = element.TransformToAncestor(viewport).Transform(new System.Windows.Point(0, 0)).Y;
            var bottom = top + element.ActualHeight;
            var shown = Math.Min(bottom, viewport.ViewportHeight) - Math.Max(top, 0);
            // Any part of the row being visible is enough to leave the list alone. The
            // threshold was ninety percent, which is precisely the case that was reported:
            // the last, half-cut row fell under it, so the request went through and the list
            // scrolled several rows to reveal what the user could already see.
            return shown > 1;
        }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Dumps the state of the three reported faults. Called at points they change.</summary>
    private void DumpDiagnostics(string when)
    {
        Diagnostics.Write("dump", $"---- {when} ----");
        Diagnostics.Combo("界面字体", UiFontBox);
        Diagnostics.Combo("颜色模式", ThemeModeBox);
        Diagnostics.Thumbs("窗口", this);
    }

    private void ApplySelectedTheme()
    {
        var theme = SelectedTheme();
        if (theme is null) return;
        var mode = _selectedThemeMode;
        WindowThemeService.ApplyResources(this, theme, mode);
        WindowThemeService.ApplyFont(this, _selectedUiFont);
        if (IsInitialized)
        {
            var backdrop = _selectedThemeBackdrop;
            WindowThemeService.ApplyBackdropOrFallback(this, backdrop, mode);
        }
        UpdateThemeSummary();
    }

    private void UpdateThemeSummary()
    {
        if (CurrentThemeNameText is null) return;
        var theme = SelectedTheme();
        if (theme is null)
        {
            CurrentThemeNameText.Text = AppLocalization.Text(_settings.Language, "没有可用主题", "No theme available");
            CurrentThemeMetaText.Text = "";
            return;
        }
        var english = AppLocalization.IsEnglish(LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language));
        CurrentThemeNameText.Text = english && !string.IsNullOrWhiteSpace(theme.Manifest.NameEn) ? theme.Manifest.NameEn : theme.Manifest.Name;
        var description = english ? theme.Manifest.DescriptionEn : theme.Manifest.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = theme.Theme.PreferredBackdrop is "mica-alt" or "acrylic"
                ? AppLocalization.Text(english ? "en-US" : "zh-CN", "半透明玻璃层次、水青高光与高对比度控件。", "Translucent glass layers, aqua highlights, and high-contrast controls.")
                : AppLocalization.Text(english ? "en-US" : "zh-CN", "系统云母底层、Fluent 控件与 BalancePet 青绿色强调色。", "System Mica, Fluent controls, and BalancePet's teal accent.");
        }
        CurrentThemeDescriptionText.Text = description;
        CurrentThemeMetaText.Text = $"{AppLocalization.Text(english ? "en-US" : "zh-CN", "主题扩展", "Theme extension")} · v{theme.Manifest.Version} · {theme.Manifest.Author}";
    }

    private void OnWindowSourceInitialized(object sender, EventArgs e) => ApplySelectedTheme();

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || !_trackChanges) return;
        var selectedItem = e.AddedItems.OfType<ComboBoxItem>().LastOrDefault();
        if (!string.IsNullOrWhiteSpace(selectedItem?.Tag?.ToString())) _selectedThemeId = selectedItem.Tag.ToString()!;
        SyncComboDisplay(ThemeBox, selectedItem?.Content?.ToString());
        var theme = SelectedTheme();
        if (theme is not null && ThemeBackdropBox is not null)
        {
            SelectByTag(ThemeBackdropBox, theme.Theme.PreferredBackdrop);
            SyncComboDisplay(ThemeBackdropBox);
        }
        ApplySelectedTheme();
        MarkSettingsDirty();
    }

    private void OnThemeOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox box)
        {
            var selectedItem = e.AddedItems.OfType<ComboBoxItem>().LastOrDefault();
            var selectedTag = selectedItem?.Tag?.ToString();
            SyncComboDisplay(box, selectedItem?.Content?.ToString());
            if (ReferenceEquals(box, ThemeModeBox) && !string.IsNullOrWhiteSpace(selectedTag)) _selectedThemeMode = selectedTag;
            if (ReferenceEquals(box, ThemeBackdropBox) && !string.IsNullOrWhiteSpace(selectedTag)) _selectedThemeBackdrop = selectedTag;
        }
        if (!_trackChanges) return;
        ApplySelectedTheme();
        MarkSettingsDirty();
    }

    private void OnOpenThemeDirectory(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_themes.RootDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _themes.RootDirectory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ThemeMessageText.Text = error.Message; }
    }

    private void OnImportThemePackage(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "BalancePet 主题 (*.zip)|*.zip", Title = "导入 BalancePet 主题" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var selectedThemeId = SelectedTag(ThemeBox, _settings.ThemeId);
            var installed = _themes.InstallThemePackage(dialog.FileName);
            RefreshThemeList(selectedThemeId);
            RefreshExtensionList();
            ThemeMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            ThemeMessageText.Text = AppLocalization.Text(_settings.Language, $"主题已安装：{installed.Manifest.Name}。可在“当前主题”中选择。", $"Theme installed: {installed.Manifest.NameEn}. Select it under Current theme.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            ThemeMessageText.Foreground = ThemeBrush("DangerBrush", System.Windows.Media.Brushes.Firebrick);
            ThemeMessageText.Text = error.Message;
        }
    }

    private void OnMinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeRestoreWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox box) SyncComboDisplay(box);
        // Only the appearance selector changes what the preview should show; the
        // others would each throw away and re-decode nine images for nothing.
        if (ReferenceEquals(sender, PetStyleBox))
        {
            // A refusal explains the appearance that was chosen a moment ago, so it
            // stops being relevant as soon as a different one is.
            PetStyleMessageText.Visibility = Visibility.Collapsed;
            RefreshPetPreview();
        }
        MarkSettingsDirty();
    }

    private void OnComboDropDownClosed(object sender, EventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox box) SyncComboDisplay(box);
    }

    private static string SelectionTag(System.Windows.Controls.ComboBox box, string fallback)
    {
        var value = SelectedTag(box, fallback);
        return value is "startup" or "daily" or "weekly" or "manual" ? value : fallback;
    }

    private void UpdatePetStyleAvailability()
    {
        if (PetStyleBox is null) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        foreach (ComboBoxItem item in PetStyleBox.Items)
        {
            if (item.Tag is not string id) continue;
            var available = PetStyleCatalog.IsAvailable(id);
            item.IsEnabled = available;
            item.ToolTip = available ? null : AppLocalization.Text(language, "素材尚未完成", "Assets are not ready");
        }
        RefreshPetPreview();
    }

    /// <summary>
    /// The nine states every appearance provides, in the order the panel shows them.
    /// </summary>
    private static readonly (string State, string Chinese, string English)[] PetPreviewStates =
    {
        ("idle", "待机", "Idle"),
        ("loading", "查询中", "Checking"),
        ("success", "查询成功", "Success"),
        ("low", "余额偏低", "Low balance"),
        ("error", "查询失败", "Failed"),
        ("clicked", "被点击", "Clicked"),
        ("codex-working", "任务进行中", "Working"),
        ("codex-done", "任务完成", "Done"),
        ("inactive", "长时间无操作", "Away"),
    };

    /// <summary>
    /// Redraws the nine-state preview of whichever appearance is selected.
    /// </summary>
    /// <remarks>
    /// The images are read from disk rather than taken from a bundled table, because
    /// the question the panel answers is "what would this installation draw". That
    /// includes an appearance that arrived as a package, and it makes a state whose
    /// artwork failed to arrive show up here instead of only in the pet window. A
    /// state that publishes extra frames is labelled with the count, so the animation
    /// contract is visible without having to wait for the pet to cycle.
    ///
    /// Building the window asks for this several times -- the style list, the
    /// availability pass and the selector each refresh it -- and every run decodes nine
    /// images on the UI thread. Only the last of those runs can be seen, so the ones
    /// before the window is ready are skipped and one is drawn at the end.
    /// </remarks>
    private void RefreshPetPreview()
    {
        if (PetPreviewGrid is null || !_trackChanges) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var style = SelectedTag(PetStyleBox, PetStyleCatalog.FallbackId);
        var directory = PetStyleCatalog.ResolveAssetDirectory(style);
        // Lets a decode that finishes late recognise that it has been superseded.
        var generation = ++_previewGeneration;
        if (!string.Equals(_previewCacheDirectory, directory, StringComparison.OrdinalIgnoreCase))
        {
            _previewCache.Clear();
            _previewCacheDirectory = directory;
        }

        PetPreviewGrid.Children.Clear();
        var pending = new List<(System.Windows.Controls.Image Target, string Path)>();
        foreach (var (state, chinese, english) in PetPreviewStates)
        {
            var frames = PetStyleCatalog.ResolveStateFrames(style, state);
            var image = new System.Windows.Controls.Image
            {
                Width = 56,
                Height = 56,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                // Without this the thumbnails are resampled with the speed-oriented
                // default and small artwork comes out noticeably rough.
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            if (frames.Count == 0) image.Opacity = 0.2;
            else pending.Add((image, frames[0]));

            var text = new StackPanel { Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = AppLocalization.Text(language, chinese, english),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (frames.Count > 1)
            {
                var framesText = new TextBlock
                {
                    Text = AppLocalization.Text(language, $"{frames.Count} 帧动画", $"{frames.Count} frames"),
                    FontSize = 10
                };
                // A resource reference rather than a lookup: the palette is swapped
                // when the theme or the system light/dark setting changes, and a
                // resolved brush would keep the colour it had when this ran.
                framesText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                text.Children.Add(framesText);
            }

            var cell = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(2, 3, 2, 3) };
            cell.Children.Add(image);
            cell.Children.Add(text);
            PetPreviewGrid.Children.Add(cell);
        }

        // The cells are laid out before their artwork exists, so the window can open
        // without waiting for nine 1024-pixel PNGs to be decoded. A frozen bitmap is
        // safe to build on a worker and hand to the UI thread afterwards.
        if (pending.Count == 0) return;
        var cache = _previewCache;
        _ = Task.Run(() =>
        {
            var decoded = pending
                .Select(item => (item.Target, Source: DecodePreviewFrame(item.Path, cache)))
                .ToArray();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // A newer refresh has already replaced these cells; assigning now would
                // paint the previous appearance over the current one.
                if (generation != _previewGeneration) return;
                foreach (var (target, source) in decoded)
                {
                    if (source is not null) target.Source = source;
                }
            }));
        });
    }

    /// <summary>
    /// Decodes one preview thumbnail at roughly the size it is drawn, reusing it while
    /// the same appearance is on screen.
    /// </summary>
    /// <remarks>
    /// Switching between appearances would otherwise decode the same nine images again
    /// each time. Called from a worker thread, which is why the cache is concurrent and
    /// the result has to be frozen.
    /// </remarks>
    private static System.Windows.Media.Imaging.BitmapImage? DecodePreviewFrame(
        string path,
        System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.Imaging.BitmapImage> cache)
    {
        if (cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            var source = new System.Windows.Media.Imaging.BitmapImage();
            source.BeginInit();
            source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            // 56 DIP is the drawn size; 128 keeps it sharp on a high-DPI display
            // without decoding the 1024-pixel original nine times over.
            source.DecodePixelWidth = 128;
            source.UriSource = new Uri(path, UriKind.Absolute);
            source.EndInit();
            source.Freeze();
            cache[path] = source;
            return source;
        }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private void ShowPetStyleMessage(string chinese, string english)
    {
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        PetStyleMessageText.Text = AppLocalization.Text(language, chinese, english);
        PetStyleMessageText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Removes the appearance that is currently chosen.
    /// </summary>
    /// <remarks>
    /// Appearance packages are downloaded from the online library but managed here,
    /// beside the selector they change, because "which shape am I using" and "do I
    /// still want it" are one question. The cases that cannot be removed are answered
    /// in place rather than by greying the button out: a reason is more use than a
    /// control that looks broken, and whether removal is allowed depends on what else
    /// is installed, which can change while the window is open.
    /// </remarks>
    private void OnUninstallSelectedAppearance(object sender, RoutedEventArgs e)
    {
        var style = SelectedTag(PetStyleBox, "");
        if (style.Length == 0) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        if (PetStyleCatalog.IsShipped(style))
        {
            ShowPetStyleMessage("这套形象随主程序提供，不能卸载。", "This appearance ships with the program and cannot be uninstalled.");
            return;
        }

        var installed = _extensions.GetInstalled()
            .FirstOrDefault(pet => string.Equals(pet.StyleId, style, StringComparison.OrdinalIgnoreCase));
        if (installed is null)
        {
            ShowPetStyleMessage("这套形象不是已安装的扩展，没有可以删除的内容。", "This appearance is not an installed extension, so there is nothing to remove.");
            return;
        }

        // The pet is the whole window, so removing the last appearance would leave a
        // blank window and an empty selector with no way back through the interface.
        if (PetStyleCatalog.IsLastAvailableStyle(style))
        {
            ShowPetStyleMessage("这是最后一套可用形象，不能卸载。请先安装或启用另一套形象。", "This is the last available appearance and cannot be uninstalled. Install or enable another one first.");
            return;
        }

        var definition = PetStyleCatalog.Get(style);
        var styleName = AppLocalization.IsEnglish(language) ? definition.EnglishName : definition.ChineseName;
        var answer = System.Windows.MessageBoxResult.Yes;   // no confirmation: the button said uninstall
        if (answer != MessageBoxResult.Yes) return;

        if (!_extensions.Uninstall(installed.Manifest.Id))
        {
            ShowPetStyleMessage("卸载失败，形象目录可能正被占用。请关闭桌宠后重试。", "Uninstall failed; the appearance directory may be in use. Close the pet and try again.");
            return;
        }

        PetStyleMessageText.Visibility = Visibility.Collapsed;
        AddInstalledPetStyles();
        UpdatePetStyleAvailability();
        // Said here rather than at the end of the method: an appearance that is not the selected
        // one returns below, and the success path used to hide this line instead of filling it.
        ShowPetStyleMessage($"已卸载：{styleName}。", $"Uninstalled: {styleName}.");
        if (PetStyleCatalog.IsAvailable(style)) return;
        // The removed appearance is still what the selector shows, and it can no
        // longer be drawn. Move to the default, which the fallback chain in the pet
        // window would have used anyway.
        SelectFirstAvailablePetStyle();
        RefreshPetPreview();

    }

    /// <summary>
    /// Marks the switch of every client that is not installed on this machine, so
    /// "not configured yet" is distinguishable from "switched off". The switch
    /// stays usable on purpose: turning it on records the intent, and the hook is
    /// written once the client appears.
    /// </summary>
    private void UpdateClientAvailability()
    {
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var warning = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);

        Mark(TaskClient.Codex, FollowCodexBox, FollowCodexHint);
        Mark(TaskClient.DeepSeekHarness, FollowDeepSeekHarnessBox, FollowDeepSeekHarnessHint);
        Mark(TaskClient.Gemini, FollowGeminiBox, FollowGeminiHint);
        Mark(TaskClient.Qwen, FollowQwenBox, FollowQwenHint);
        Mark(TaskClient.Claude, FollowClaudeBox, FollowClaudeHint);

        void Mark(TaskClient client, System.Windows.Controls.CheckBox box, System.Windows.Controls.TextBlock hint)
        {
            if (box is null || hint is null) return;
            if (ClientHookInstaller.IsClientPresent(client))
            {
                box.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                hint.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }
            box.Foreground = warning;
            hint.Text = AppLocalization.Text(language,
                $"未检测到 {ClientHookInstaller.DisplayName(client)}。仍可开启，安装它之后会自动写入联动配置。",
                $"{ClientHookInstaller.DisplayName(client)} is not installed. The switch still works; its hook is written once the client appears.");
            hint.Visibility = System.Windows.Visibility.Visible;
        }
    }

    private void RefreshExtensionList()
    {
        if (ExtensionListBox is null) return;
        var pets = _extensions.GetInstalled();
        var features = _featureExtensions.GetInstalled();
        var themes = _themes.GetInstalled();
        var entries = _extensionLibrary.BuildEntries(pets, features, themes).ToList();
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        foreach (var entry in entries) entry.IsEnglish = AppLocalization.IsEnglish(language);
        ExtensionListBox.ItemsSource = null;
        ExtensionListBox.ItemsSource = entries
            .OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        UpdateExtensionButtons();
        ExtensionUpdateService.ApplyCachedUpdates(entries, _extensionUpdates.LoadCache());
        ExtensionListBox.ItemsSource = null;
        // Appearances are installed from here but chosen on the pet page, so they are
        // not listed again: a second place to manage the same thing invites the
        // question of which one is authoritative, and "enable/disable" means nothing
        // for a pet. Feature extensions and themes are only managed here, so they stay.
        ExtensionListBox.ItemsSource = entries
            .Where(entry => !string.Equals(entry.Type, "pet", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.DisplayLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        RebuildPluginCatalogItems();
    }

    private void UpdateExtensionButtons()
    {
        // Row-level action buttons are data-bound in the item template.
    }

    private void OnInstallOrUninstallExtension(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not ExtensionCatalogEntry selected) return;
        if (selected.IsInstalled) OnUninstallExtension(sender, e);
        else OnInstallSelectedExtension(sender, e);
    }

    private async void OnUpdateExtension(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not ExtensionCatalogEntry selected || (!selected.HasUpdate && selected.Package is null)) return;
        try
        {
            var packagePath = selected.Package?.PackagePath ?? "";
            if (selected.RemoteUpdate is not null && selected.HasRemoteUpdate)
            {
                var updateLanguage = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
selected.Busy = true;
selected.BusyIndeterminate = true;
EnsureStallTimer();
    MarkBusyProgress(selected.Id);
    selected.BusyTextBrush = TryFindResource("AccentBrush") as System.Windows.Media.Brush;
    selected.BusyBarBrush = selected.BusyTextBrush;
    AnnounceDownload(selected.DisplayLabel, AppLocalization.Text(updateLanguage, "下载中", "Downloading"),
        AppLocalization.Text(updateLanguage, "关掉窗口也没关系，下完再叫你", "Close the window if you like; I'll tell you when it's done"));
    selected.BusyText = AppLocalization.Text(updateLanguage, "正在准备下载…", "Preparing the download…");
selected.BusyDetail = "";
var localDownloadProgress = new Progress<double>(fraction =>
{
    selected.BusyText = AppLocalization.Text(updateLanguage, "正在下载更新包…", "Downloading the update package…");
    MarkBusyProgress(selected.Id);
     selected.BusyTextBrush = TryFindResource("AccentBrush") as System.Windows.Media.Brush;
    selected.BusyBarBrush = selected.BusyTextBrush;
    selected.BusyIndeterminate = false;
    selected.BusyPercent = Math.Clamp(fraction * 100, 0, 100);
    selected.BusyDetail = $"{selected.BusyPercent:0}%";
});
try
{
    packagePath = await _extensionUpdates.DownloadAsync(selected.RemoteUpdate, default, localDownloadProgress);
    selected.BusyIndeterminate = true;
    selected.BusyText = AppLocalization.Text(updateLanguage, "正在校验完整性…", "Verifying the package…");
     EndBusy(selected.Id, failed: false, "", selected);
    AnnounceDownload(selected.DisplayLabel, AppLocalization.Text(updateLanguage, "安装完成", "Installed"), "");
}
 catch (Exception error)
{
     EndBusy(selected.Id, failed: true, error.Message, selected);
    AnnounceDownload(selected.DisplayLabel, AppLocalization.Text(updateLanguage, "下载失败", "Download failed"), error.Message);
     throw;
}
                try { _extensionLibrary.ImportPackage(packagePath); } finally { try { File.Delete(packagePath); } catch (IOException) { } }
                packagePath = _extensionLibrary.Scan().First(value => string.Equals(value.Id, selected.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Version, selected.RemoteUpdate.Version, StringComparison.OrdinalIgnoreCase)).PackagePath;
            }
            if (string.IsNullOrWhiteSpace(packagePath)) return;
            if (selected.Type == "feature")
            {
                var installedFeature = _featureExtensions.InstallFeaturePackage(packagePath);
                StartBackgroundFeatureIfNeeded(installedFeature);
            }
            else if (selected.Type == "theme")
            {
                var selectedThemeId = SelectedTag(ThemeBox, _settings.ThemeId);
                var installedTheme = _themes.InstallThemePackage(packagePath);
                RefreshThemeList(selectedThemeId);
                if (string.Equals(selectedThemeId, installedTheme.Manifest.Id, StringComparison.OrdinalIgnoreCase)) ApplySelectedTheme();
            }
            else _extensions.InstallPetPackage(packagePath);
            RefreshExtensionList();
            ExtensionMessageText.Text = AppLocalization.Text(_settings.Language, "扩展已更新。", "Extension updated.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException or JsonException)
        { ShowExtensionError($"更新失败：{error.Message}", $"Update failed: {error.Message}"); }
    }

    private async void OnCheckExtensionUpdates(object sender, RoutedEventArgs e)
    {
        await CheckExtensionUpdatesAsync(true);
    }

    private async Task CheckExtensionUpdatesAsync(bool manual)
    {
        try
        {
            var entries = ExtensionListBox.Items.OfType<ExtensionCatalogEntry>().ToArray();
            var result = await _extensionUpdates.CheckAsync(entries);
            _settings.LastExtensionUpdateCheckUtc = DateTimeOffset.UtcNow;
            _store.Save(_settings);
            var found = result.Releases.Values.Count(value => entries.Any(entry => string.Equals(entry.Id, value.Id, StringComparison.OrdinalIgnoreCase) && entry.IsInstalled && ExtensionCatalogEntry.CompareVersions(value.Version, entry.InstalledVersion) > 0));
            ExtensionUpdateStatusText.Text = found > 0 ? AppLocalization.Text(_settings.Language, $"发现 {found} 个扩展更新。", $"{found} extension update(s) available.") : AppLocalization.Text(_settings.Language, "扩展已是最新版本。", "All extensions are up to date.");
            RefreshExtensionList();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ExtensionUpdateStatusText.Text = AppLocalization.Text(_settings.Language, $"扩展更新检查失败：{error.Message}", $"Extension update check failed: {error.Message}");
            if (manual) ShowExtensionError($"扩展更新检查失败：{error.Message}", $"Extension update check failed: {error.Message}");
        }
    }

    private void OnExtensionSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateExtensionButtons();

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _suppressChangeTracking = true;
        try
        {
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            AppLocalization.Apply(this, language);
            RefreshLanguageSelector(language, selectLanguage: false);
            SyncAllComboDisplays();
            UpdatePresetUi(false);
            UpdatePetStyleAvailability();
            UpdateClientAvailability();
            UpdateExtensionButtons();
            RefreshExtensionActionLabels(language);
            RefreshPluginCatalogLabels(language);
            RefreshVersionPanelLabels(language);
        }
        finally
        {
            _suppressChangeTracking = false;
            _hasUnsavedChanges = false;
        }
        await LoadPluginCatalogAsync(manual: false);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.TabControl tabs || tabs.SelectedItem is not TabItem selectedTab) return;
        _suppressChangeTracking = true;
        try
        {
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            AppLocalization.Apply(selectedTab, language);
            SyncAllComboDisplays();
            // Redrawn after the pass above, which translates by looking a label's text up in the
            // dictionary: a result naming a release is not in it, and would stay in the language
            // it was written in.
            RefreshVersionPanelLabels(language);
        }
        finally { _suppressChangeTracking = false; }
        AnimatePageChange();
    }

    /// <summary>
    /// Shows or hides an element, fading it in when the animation switch is on.
    /// </summary>
    /// <remarks>
    /// Hiding is instant on purpose. Sliding something out keeps it in the layout while it
    /// animates, so everything below it moves twice -- once when it starts and once when the
    /// collapse finally lands. Appearing has no such problem, and appearing is the half a
    /// person actually notices.
    /// </remarks>
    private void RevealElement(UIElement element, bool visible)
    {
        if (element is null) return;
        if (!visible)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        element.Visibility = Visibility.Visible;
        if (NavigationAnimationsBox?.IsChecked != true || element is not FrameworkElement target) return;

        target.Opacity = 0;
        target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    /// <summary>
    /// Fades the page in and slides it a few pixels, in the direction the sidebar reads.
    /// </summary>
    /// <remarks>
    /// This is the same switch the sidebar collapse already answers to. Before it, that
    /// switch controlled exactly one animation in the whole window, which made it look
    /// redundant rather than optional -- a setting that turns off a single effect is not
    /// worth its own row.
    ///
    /// A short slide rather than a long one: the content is a form, and moving a form far
    /// enough to be noticed is long enough to be in the way when switching pages to compare
    /// two values.
    /// </remarks>
    private void AnimatePageChange()
    {
        if (SettingsContentHost is null || SettingsContentShift is null) return;
        if (NavigationAnimationsBox?.IsChecked != true)
        {
            SettingsContentHost.BeginAnimation(OpacityProperty, null);
            SettingsContentShift.BeginAnimation(TranslateTransform.XProperty, null);
            SettingsContentHost.Opacity = 1;
            SettingsContentShift.X = 0;
            return;
        }

        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        SettingsContentShift.BeginAnimation(TranslateTransform.XProperty, slide);
        SettingsContentHost.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Passes the wheel on to whatever contains this list.
    /// </summary>
    /// <remarks>
    /// The lists no longer scroll: the page does, and they size to their content. But a
    /// ListBox still handles the wheel whether or not it has anywhere to scroll, so the
    /// pointer resting on a card -- which is most of the page -- stopped the page from
    /// scrolling at all. Turning the scrollbar off only stops it being drawn.
    ///
    /// The event is re-raised on the visual parent rather than left unhandled, because by
    /// the time it reaches here the ListBox has already claimed it.
    /// </remarks>
    private void OnListPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element) return;
        e.Handled = true;
        var forwarded = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = element
        };
        (System.Windows.Media.VisualTreeHelper.GetParent(element) as UIElement)?.RaiseEvent(forwarded);
    }

    private void OnScanExtensionLibrary(object sender, RoutedEventArgs e)
    {
        try
        {
            _extensionLibrary.EnsureDirectory();
            RefreshExtensionList();
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            ExtensionMessageText.Text = AppLocalization.Text(language, "扩展库扫描完成。可使用每行右侧的图标操作。", "Extension library scanned. Use the icons on each row to manage extensions.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowExtensionError($"扫描扩展库失败：{error.Message}", $"Failed to scan extension library: {error.Message}");
        }
    }

    private void RefreshExtensionActionLabels(string language)
    {
        if (ExtensionScanButton is null) return;
        ExtensionScanButton.ToolTip = AppLocalization.Text(language, "扫描扩展库", "Scan extension library");
        ExtensionOpenButton.ToolTip = AppLocalization.Text(language, "打开扩展库", "Open extension library");
        ExtensionImportButton.ToolTip = AppLocalization.Text(language, "导入 ZIP 到扩展库", "Import ZIP to extension library");
        ExtensionCleanupButton.ToolTip = AppLocalization.Text(language, "清理旧版扩展资源", "Remove old extension versions");
        ExtensionCheckButton.ToolTip = AppLocalization.Text(language, "检查扩展更新", "Check extension updates");
    }

    /// <summary>
    /// What the version row's own update check last reported, and the release it named.
    /// </summary>
    /// <remarks>
    /// Kept as state rather than left in the label, because switching language redraws the row
    /// from what the check found. The result sentence carries a version number, so it cannot be
    /// translated by looking the label's text up in the dictionary the way a fixed string can:
    /// that sentence is not in the dictionary, and a switch to English would leave it in Chinese.
    /// </remarks>
    private string _panelUpdateState = "";
    private string _panelUpdateRelease = "";

    /// <summary>
    /// The version this window shows, and the version its update check compares against.
    /// </summary>
    /// <remarks>
    /// Read from the assembly rather than written out beside the label: a second copy of a
    /// version number is a copy that goes stale the moment the build stamps a new one, and the
    /// check would then compare a release against a version this program does not have.
    ///
    /// The informational version is what the build carries. It can hold build metadata after a
    /// '+', which is not part of the version, so that suffix is dropped. The assembly version is
    /// the fallback for an assembly that carries no informational one.
    ///
    /// Taken from this type's assembly and not from the entry assembly, because this window is
    /// also rendered by the screenshot tool: there the entry assembly is the tool, and reading
    /// it would report the tool's version instead of the program's.
    /// </remarks>
    private static string CurrentVersionText()
    {
        var assembly = typeof(SettingsWindow).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];

        var version = assembly.GetName().Version;
        if (version is null) return "";
        return version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// Draws the version row: the version, the button's label, and the last result.
    /// </summary>
    /// <remarks>
    /// Reading the state instead of being told what to write is what lets a language change
    /// redraw a result that is already on screen.
    /// </remarks>
    private void RefreshVersionPanelLabels(string language)
    {
        if (AppVersionText is null) return;
        AppVersionText.Text = CurrentVersionText();
        PanelUpdateButton.Content = AppLocalization.Text(language, "检查更新", "Check for updates");
        PanelUpdateText.Text = _panelUpdateState switch
        {
            "checking" => AppLocalization.Text(language, "检查中…", "Checking…"),
            "latest" => AppLocalization.Text(language, "已是最新", "Up to date"),
            "found" => AppLocalization.Text(language, $"发现新版本 {_panelUpdateRelease}", $"New version available: {_panelUpdateRelease}"),
            "failed" => AppLocalization.Text(language, "检查失败", "Check failed"),
            _ => ""
        };
    }

    /// <summary>
    /// Checks for a program update from the panel, and answers inside the panel.
    /// </summary>
    /// <remarks>
    /// The service is built and called here rather than borrowed from the pet's own check: that
    /// path answers with a speech bubble, and this one must not. Nothing about the pet's
    /// right-click menu, its tray entry or its bubble is touched by this.
    /// </remarks>
    private async void OnPanelUpdateClick(object sender, RoutedEventArgs e)
    {
        if (PanelUpdateButton is null || !PanelUpdateButton.IsEnabled) return;
        PanelUpdateButton.IsEnabled = false;
        PanelUpdateBar.Visibility = Visibility.Visible;
        _panelUpdateState = "checking";
        RefreshVersionPanelLabels(LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language));
        try
        {
            var release = await new UpdateService(_extensionUpdateHttpClient).CheckAsync(CurrentVersionText());
            _panelUpdateRelease = release?.TagName ?? "";
            _panelUpdateState = release is null ? "latest" : "found";
        }
        catch (Exception)
        {
            // Every failure reads the same here. A relay briefly unreachable, an allowance spent
            // by a shared address and a malformed answer are one thing to somebody who asked
            // whether a newer version exists: the question went unanswered. Caught as Exception
            // because this is an async void handler, and anything that escapes it ends the
            // process rather than the check.
            _panelUpdateRelease = "";
            _panelUpdateState = "failed";
        }
        finally
        {
            PanelUpdateBar.Visibility = Visibility.Collapsed;
            PanelUpdateButton.IsEnabled = true;
            // Read again rather than reused from before the await: the check takes as long as
            // the network takes, and the language can change while it runs.
            RefreshVersionPanelLabels(LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language));
        }
    }

    private readonly HashSet<string> _pluginCatalogBusyIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pictures the catalog entries point at, fetched once each.</summary>
    private readonly PluginIconService _pluginIcons = new();

    /// <summary>Shows how much the icon cache is using. Called on load and after clearing.</summary>
    private void UpdateIconCacheText()
    {
        var bytes = _pluginIcons.CacheSizeBytes();
        IconCacheText.Text = bytes <= 0
            ? "当前没有缓存。"
            : $"当前占用 {FormatCacheSize(bytes)}。";
    }

    private static string FormatCacheSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.0} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>
    /// Empties the icon cache, after asking.
    /// </summary>
    /// <remarks>
    /// Asked because it is not undoable and the cost is paid later, on a page the user is not
    /// looking at: the next visit to the extension list downloads every icon again. The number is
    /// refreshed from the directory afterwards rather than recomputed in the head, so what the
    /// page shows is what is actually on disk.
    /// </remarks>
    private void OnClearIconCache(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show(
            this,
            "清除后，下次打开扩展列表会重新下载这些图标。现在清除吗？",
            "清除图标缓存",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;
        _pluginIcons.ClearCache();
        UpdateIconCacheText();
    }

    /// <summary>The rows currently on screen, so a picture that arrives can reach them.</summary>
    /// <remarks>
    /// Rebuilding the list when an icon lands would be simpler and wrong: the catalog
    /// holds twenty entries and sixteen pictures arrive one after another, so the list
    /// would be rebuilt sixteen times and jump back to the top each time. The rows are
    /// made observable instead, and the fetch fills them in place.
    /// </remarks>
    private readonly Dictionary<string, PluginCatalogItemView> _pluginIconRows = new(StringComparer.OrdinalIgnoreCase);
    private bool _pluginIconsLoading;

    /// <summary>Decoded preview thumbnails, dropped when the appearance changes.</summary>
    /// <remarks>
    /// Concurrent because the decoding happens on a worker: building nine frozen
    /// bitmaps takes about a quarter of a second, which is time the window would
    /// otherwise spend not being on screen.
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.Imaging.BitmapImage> _previewCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _previewCacheDirectory;
    private int _previewGeneration;

    /// <summary>Empty for "all", otherwise the record <c>type</c> being shown.</summary>
    private string _pluginCatalogCategory = "";

    /// <summary>
    /// Narrows the catalog to one kind of extension.
    /// </summary>
    /// <remarks>
    /// A TabControl selects its first tab while the XAML is still being read, which
    /// raises this before the settings object and the list it feeds have been
    /// assigned. <see cref="_trackChanges"/> is what tells that apart from a click,
    /// because it is set once the constructor is done.
    /// </remarks>
    private void OnPluginCatalogCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_trackChanges) return;
        _pluginCatalogCategory = (PluginCatalogCategoryTabs.SelectedItem as TabItem)?.Tag?.ToString() ?? "";
        RebuildPluginCatalogItems();
    }

    private void RefreshPluginCatalogLabels(string language)
    {
        if (OnlineLibraryTabText is null) return;
        ExtensionManagementTitleText.Text = AppLocalization.Text(language, "扩展管理", "Extension management");
        ExtensionManagementHintText.Text = AppLocalization.Text(language, "在线插件库负责发现扩展；功能扩展安装即启用，后台能力会自动运行。", "Discover extensions online; feature extensions enable on installation, and background capabilities start automatically.");
        OnlineLibraryTabText.Text = AppLocalization.Text(language, "在线插件库", "Online plugin catalog");
        PluginCatalogHintText.Text = AppLocalization.Text(language, "从 BalancePet 官方目录发现插件；下载后仍会执行本地安全校验。", "Discover plugins from the curated BalancePet catalog; every download is still verified locally.");
        PluginCatalogRefreshButton.Content = AppLocalization.Text(language, "刷新目录", "Refresh catalog");
        PluginCatalogRefreshButton.ToolTip = AppLocalization.Text(language, "刷新在线插件目录", "Refresh the online plugin catalog");
        PluginCatalogSearchBox.ToolTip = AppLocalization.Text(language, "搜索插件名称、作者或分类", "Search by plugin name, author, or category");
        CatalogCategoryAllText.Text = AppLocalization.Text(language, "全部", "All");
        CatalogCategoryFeatureText.Text = AppLocalization.Text(language, "功能", "Features");
        CatalogCategoryPetText.Text = AppLocalization.Text(language, "形象", "Appearances");
        CatalogCategoryThemeText.Text = AppLocalization.Text(language, "主题", "Themes");
        CatalogCategoryBrowserText.Text = AppLocalization.Text(language, "浏览器", "Browser");
        LocalExtensionsTabText.Text = AppLocalization.Text(language, "本地扩展功能", "Local extension features");
        LocalExtensionsHintText.Text = AppLocalization.Text(language, "可将扩展 ZIP 拖到此处直接安装；扩展库中的 ZIP 也会显示在这里。安装后的形象在「桌宠与交互」里选择。", "Drop an extension ZIP here to install it; ZIPs in the extension library also appear here. Installed appearances are chosen under Pet & interaction.");
    }

    private async void OnRefreshPluginCatalog(object sender, RoutedEventArgs e)
        => await LoadPluginCatalogAsync(manual: true);

    private void OnPluginCatalogSearchChanged(object sender, TextChangedEventArgs e)
        => RebuildPluginCatalogItems();

    private async Task LoadPluginCatalogAsync(bool manual)
    {
        if (PluginCatalogRefreshButton is null) return;
        PluginCatalogRefreshButton.IsEnabled = false;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        PluginCatalogStatusText.Text = AppLocalization.Text(language, "正在读取在线插件目录…", "Loading the online plugin catalog…");
        try
        {
            var result = await _pluginCatalog.LoadAsync(_pluginCatalogCancellation.Token);
            if (_pluginCatalogCancellation.IsCancellationRequested) return;
            _pluginCatalogEntries = result.Entries;
            // Read before the list is built, so the rows that can be drawn from the
            // disk cache are drawn in the first paint rather than a frame later.
            _pluginIcons.LoadFromDisk(_pluginCatalogEntries);
            RebuildPluginCatalogItems();
            if (result.FromRemote)
            {
                // A source that failed while another succeeded is still worth saying:
                // an empty category looks like "nothing is published" otherwise.
                var partial = string.IsNullOrWhiteSpace(result.Error) ? "" : $"（{result.Error}）";
                // Said out loud because the mirror caches a branch for hours: a catalog
                // that looks a version behind is the mirror, not a mistake.
                var via = result.Mirrored ? AppLocalization.Text(language, "（经镜像）", " (via mirror)") : "";
                PluginCatalogStatusText.Text = AppLocalization.Text(language,
                    $"已从官方目录加载 {_pluginCatalogEntries.Count} 个扩展。{partial}{via}",
                    $"Loaded {_pluginCatalogEntries.Count} extension(s) from the curated catalogs.{(string.IsNullOrWhiteSpace(result.Error) ? "" : $" {result.Error}")}{(result.Mirrored ? " (via mirror)" : "")}");
            }
            else if (_pluginCatalogEntries.Count > 0)
            {
                var suffix = string.IsNullOrWhiteSpace(result.Error) ? "" : $"（在线目录暂时不可用：{result.Error}）";
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"网络不可用，已使用本地缓存，共 {_pluginCatalogEntries.Count} 个扩展{suffix}", $"Online catalogs unavailable; showing {_pluginCatalogEntries.Count} cached extension(s).{(string.IsNullOrWhiteSpace(result.Error) ? "" : $" {result.Error}")}");
            }
            else
            {
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"扩展目录加载失败：{result.Error ?? "暂无可用条目"}", $"Could not load the catalogs: {result.Error ?? "No entries are available."}");
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException
            or TaskCanceledException or OperationCanceledException or ObjectDisposedException)
        {
            // OperationCanceledException and ObjectDisposedException are the two shapes this
            // load takes when the window closed while it was still running: the first from
            // the token being cancelled, the second from the client the window disposed
            // under it. Both mean "nobody is waiting for this any more", and neither is
            // worth ending the process for — which is what happened, with the event log
            // pointing at this method and a disposed HttpClient.
            if (!_pluginCatalogCancellation.IsCancellationRequested)
            {
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"扩展目录加载失败：{error.Message}", $"Could not load the catalogs: {error.Message}");
                if (manual) ShowExtensionError($"扩展目录加载失败：{error.Message}", $"Could not load the catalogs: {error.Message}");
            }
        }
        finally
        {
            if (!_pluginCatalogCancellation.IsCancellationRequested) PluginCatalogRefreshButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Starts fetching the pictures the rows on screen are waiting for.
    /// </summary>
    /// <remarks>
    /// One at a time and in list order, so the rows at the top fill in first and a
    /// catalog of twenty costs twenty small requests rather than twenty at once. The
    /// pass is not restarted while one is running; the pump asks again when it
    /// finishes, which is what covers a row that appeared mid-fetch, such as after
    /// switching the category filter.
    /// </remarks>
    private void QueuePluginIconFetch()
    {
        if (_pluginIconsLoading || !HasPendingPluginIcons()) return;
        _ = FetchPluginIconsAsync();
    }

    private bool HasPendingPluginIcons()
        => _pluginIconRows.Values.Any(view => _pluginIcons.NeedsWork(view.Record, view.InstalledStyle));

    /// <summary>
    /// Maps each installed appearance's package id to its appearance id.
    /// </summary>
    /// <remarks>
    /// Built once per rebuild rather than asked per row. The answer comes from the
    /// installed packages — the appearance id a package supplies is its manifest's
    /// <c>style</c>, and the convention that a package is called <c>pet.&lt;style&gt;</c>
    /// is not a guarantee — and asking is not cheap: it enumerates the package
    /// directories and reads their manifests, which is not something to repeat for
    /// every one of twenty rows.
    /// </remarks>
    private static Dictionary<string, string> InstalledStyleByPackage()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PetStyleCatalog.GetAvailableStyles())
        {
            var packageId = PetStyleCatalog.GetExtensionStyleId(definition.Id);
            if (!string.IsNullOrWhiteSpace(packageId)) map[packageId] = definition.Id;
        }
        return map;
    }

    private async Task FetchPluginIconsAsync()
    {
        _pluginIconsLoading = true;
        try
        {
            while (!_pluginCatalogCancellation.IsCancellationRequested)
            {
                var view = _pluginIconRows.Values.FirstOrDefault(row => _pluginIcons.NeedsWork(row.Record, row.InstalledStyle));
                if (view is null) break;
                var image = await _pluginIcons.ResolveAsync(_extensionUpdateHttpClient, view.Record, view.InstalledStyle, _pluginCatalogCancellation.Token);
                if (_pluginIconRows.TryGetValue(view.Id, out var current)) current.SetIcon(image);
            }
        }
        finally
        {
            _pluginIconsLoading = false;
            // A row that arrived while this pass was running was never in the list it
            // walked, and nothing else would come back for it: it would turn forever.
            if (HasPendingPluginIcons()) QueuePluginIconFetch();
        }
    }

    private void RebuildPluginCatalogItems()
    {
        if (PluginCatalogListBox is null) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var english = AppLocalization.IsEnglish(language);
        var installed = _extensionLibrary.BuildEntries(_extensions.GetInstalled(), _featureExtensions.GetInstalled(), _themes.GetInstalled())
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var query = PluginCatalogSearchBox?.Text?.Trim() ?? "";
        var styleByPackage = InstalledStyleByPackage();
        var views = _pluginCatalogEntries
            .Where(record => _pluginCatalogCategory.Length == 0
                || string.Equals(record.Type, _pluginCatalogCategory, StringComparison.OrdinalIgnoreCase))
            .Where(record => string.IsNullOrWhiteSpace(query) || string.Join(" ", record.Id, record.Name, record.NameEn, record.Author, record.Description, record.DescriptionEn, string.Join(" ", record.Categories)).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .Select(record =>
            {
                installed.TryGetValue(record.Id, out var local);
                var style = styleByPackage.TryGetValue(record.Id, out var found) ? found : null;
                var busyNow = _pluginCatalogBusyIds.Contains(record.Id);
        // Also busy when an install started in a window that has since been closed: the work
        // outlives the window, so the row has to as well, or it offers a second Install.
        busyNow = busyNow || InstallingPackages.Contains(record.Id);
                  var view = new PluginCatalogItemView(record, local, english, style)
                  {
                      IsBusy = busyNow,
                      // The row objects are rebuilt whenever the list is refreshed, and a rebuild
                      // happens right after an install starts. Carrying the state across is what
                      // keeps the progress block on screen: without it the new row is idle and
                      // the work in flight becomes invisible again, which is the bug this whole
                      // change exists to fix.
                      Busy = busyNow,
                      BusyText = busyNow ? _pluginCatalogBusyText : "",
                      BusyIndeterminate = true
                  };
                // A picture already on the disk is shown straight away, so a second
                // visit to this page has nothing turning in it. An appearance that is
                // installed here answers from its own artwork in the same pass.
                var icon = _pluginIcons.Cached(record);
                if (icon is not null) view.SetIcon(icon);
                else view.SetIconBusy(true);
                return view;
            })
            .ToArray();
        _pluginIconRows.Clear();
        foreach (var view in views) _pluginIconRows[view.Id] = view;
        PluginCatalogListBox.ItemsSource = views;
      // A row rebuilt mid-download shows the number the download is actually at, rather than a
      // spinner that says only that something is happening.
      foreach (var view in views)
      {
          // The sentence first, and whether or not any progress has arrived yet: a row rebuilt
          // in the moment between the click and the first byte is still an install in progress,
          // and showing a bare empty bar for that moment is what made it look like nothing was
          // happening.
          if (view.Busy) view.BusyText = AppLocalization.Text(_settings.Language, "正在下载更新包…", "Downloading the update package…");
          if (!InstallProgress.TryGetValue(view.Id, out var live)) continue;
          view.BusyPercent = System.Math.Clamp(live * 100, 0, 100);
          view.BusyDetail = $"{view.BusyPercent:0}%";
          view.BusyIndeterminate = false;
          // The phase line as well: it lived in the window that started the download, so a row
          // rebuilt here had none -- the number was live while the sentence above it was missing.
          if (string.IsNullOrEmpty(view.BusyText)) view.BusyText = AppLocalization.Text(_settings.Language, "正在下载更新包…", "Downloading the update package…");
      }
        QueuePluginIconFetch();
        // Two true numbers that read as a contradiction when a category is selected,
        // so the count says which is which rather than leaving the user to guess.
        PluginCatalogCountText.Text = views.Length == _pluginCatalogEntries.Count
            ? AppLocalization.Text(language, $"{views.Length} 个扩展", $"{views.Length} extension(s)")
            : AppLocalization.Text(language, $"{views.Length} / {_pluginCatalogEntries.Count} 个扩展", $"{views.Length} / {_pluginCatalogEntries.Count} extension(s)");

        // The list keeps its height whether or not it has rows, so an empty result has
        // to say something: a blank box of that size looks like a failed load.
        var empty = views.Length == 0;
        PluginCatalogEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty) return;
        PluginCatalogEmptyText.Text = _pluginCatalogEntries.Count == 0
            ? AppLocalization.Text(language,
                "目录里暂时没有可安装的扩展。点「刷新目录」重新读取。",
                "No installable extensions in the catalog yet. Use Refresh catalog to read it again.")
            : _pluginCatalogCategory switch
            {
                "pet" => AppLocalization.Text(language,
                    "还没有可安装的形象包。主程序自带两套形象与一套占位形象，更多形象发布后会出现在这里。",
                    "No installable appearances yet. The program ships with two appearances and a placeholder; more will appear here once published."),
                _ => AppLocalization.Text(language,
                    "这个分类下暂时没有内容。",
                    "Nothing in this category yet.")
            };
    }

    /// <summary>Text kept beside the busy ids so a rebuilt row can show what is happening.</summary>
    private string _pluginCatalogBusyText = "";
    private bool _installingPackage;
    /// <summary>Packages being installed right now, shared by every settings window: the work outlives the window that started it.</summary>
    /// <summary>Raised when an install finishes, wherever it was started: the window that began it may be gone.</summary>
    internal static event Action? PackageInstallsChanged;

    /// <summary>Raised on every progress report, with the package id and the fraction done.</summary>
    internal static event Action<string, double>? PackageProgressChanged;

    /// <summary>Live progress of the installs in flight, so a window opened mid-download shows numbers, not a spinner.</summary>
    private static readonly Dictionary<string, double> InstallProgress = new(StringComparer.Ordinal);

    private static readonly HashSet<string> InstallingPackages = new(StringComparer.Ordinal);

    /// <summary>
    /// The row that is on screen for the same entry as <paramref name="fallback"/>.
    /// </summary>
    /// <remarks>
    /// The list is rebuilt while an install is running, so the object the click started on is
    /// no longer the one being displayed. Progress written to it goes nowhere.
    /// </remarks>
    private PluginCatalogItemView Row(PluginCatalogItemView? fallback = null)
        => fallback is not null && _pluginIconRows.TryGetValue(fallback.Id, out var live) ? live : fallback!;

    /// <summary>When each busy row last received a progress report, for the stall detector.</summary>
    private readonly Dictionary<string, DateTime> _busyLastProgress = new(StringComparer.Ordinal);
    private System.Windows.Threading.DispatcherTimer? _busyStallTimer;

    /// <summary>
    /// Turns a stall into something visible.
    /// </summary>
    /// <remarks>
    /// The progress callback only runs when bytes arrive, so silence is exactly the case it can
    /// never report: a download that has stopped producing data looks identical to one that is
    /// working. A timer is the only thing that can tell them apart, and it is what turns the
    /// phase line amber and says how long the wait has been.
    /// </remarks>
    private void EnsureStallTimer()
    {
        if (_busyStallTimer is not null) return;
        _busyStallTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _busyStallTimer.Tick += (_, _) =>
        {
            var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush;
            var warn = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB7, 0x79, 0x1F));
            foreach (var list in new[] { PluginCatalogListBox, ExtensionListBox })
            {
                foreach (var entry in list.Items)
                {
                    var id = entry switch
                    {
                        PluginCatalogItemView v => v.Id,
                        ExtensionCatalogEntry e => e.Id,
                        _ => null
                    };
                    if (id is null) continue;
                    var busy = entry switch { PluginCatalogItemView v2 => v2.Busy, ExtensionCatalogEntry e2 => e2.Busy, _ => false };
                    if (!busy) { _busyLastProgress.Remove(id); continue; }
                    if (!_busyLastProgress.TryGetValue(id, out var last)) { _busyLastProgress[id] = DateTime.UtcNow; continue; }
                    var seconds = (int)(DateTime.UtcNow - last).TotalSeconds;
                    if (seconds < 8) continue;
                    var text = AppLocalization.Text(_settings.Language, $"已 {seconds} 秒没有新数据，仍在等待…", $"No new data for {seconds} seconds; still waiting…");
                    switch (entry)
                    {
                        case PluginCatalogItemView v3: v3.BusyText = text; v3.BusyTextBrush = warn; v3.BusyBarBrush = warn; break;
                        case ExtensionCatalogEntry e3: e3.BusyText = text; e3.BusyTextBrush = warn; e3.BusyBarBrush = warn; break;
                    }
                }
            }
            if (_busyLastProgress.Count == 0) _busyStallTimer?.Stop();
        };
        _busyStallTimer.Start();
    }

    private void MarkBusyProgress(string id) => _busyLastProgress[id] = DateTime.UtcNow;

    private void EndBusy(string id, bool failed, string reason, object row)
    {
        InstallingPackages.Remove(id);
        InstallProgress.Remove(id);
        PackageInstallsChanged?.Invoke();
        _busyLastProgress.Remove(id);
        var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush;
        var fail = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
        var text = failed ? AppLocalization.Text(_settings.Language, $"更新失败：{reason}", $"Update failed: {reason}") : "";
        switch (row)
        {
            case PluginCatalogItemView v:
                v.Busy = failed;
                v.BusyTextBrush = failed ? fail : accent;
                v.BusyBarBrush = failed ? fail : accent;
                if (failed) { v.BusyText = text; v.BusyIndeterminate = true; }
                break;
            case ExtensionCatalogEntry e:
                e.Busy = failed;
                e.BusyTextBrush = failed ? fail : accent;
                e.BusyBarBrush = failed ? fail : accent;
                if (failed) { e.BusyText = text; e.BusyIndeterminate = true; }
                break;
        }
    }
    /// <summary>Tells the pet that a download is happening, or how it ended.</summary>
    /// <remarks>
    /// One bubble when it starts and one when it finishes, with the state as the prominent line.
    /// Percentages live in the row inside the panel: a bubble that rewrote itself every few
    /// seconds was reporting a number nobody asked it for, and the pet is the wrong place to
    /// watch a progress bar.
    /// </remarks>
    private void AnnounceDownload(string name, string state, string detail)
        => (System.Windows.Application.Current.MainWindow as MainWindow)?.ShowDownloadProgress(name, state, detail);
    private async void OnInstallPluginCatalogItem(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not PluginCatalogItemView item || !item.CanInstall || !_pluginCatalogBusyIds.Add(item.Id)) return;
        RebuildPluginCatalogItems();
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        // Refused here, not only in the UI: closing and reopening the panel used to lose the
        // busy state, so the row offered Install again for a package that was still downloading --
        // and the second download and the first then fought over the same half-written file.
        if (!InstallingPackages.Add(item.Id))
        {
            ExtensionMessageText.Text = AppLocalization.Text(language, "这个扩展正在安装中，请稍候。", "This extension is already being installed; please wait.");
            return;
        }
        try
        {
            var record = item.Record;
            var liveRow = Row(item);
      Row(item).BusyIndeterminate = true;
       AnnounceDownload(item.Record.Name, AppLocalization.Text(language, "下载中", "Downloading"),
          AppLocalization.Text(language, "关掉窗口也没关系，下完再叫你", "Close the window if you like; I'll tell you when it's done"));
      _pluginCatalogBusyText = AppLocalization.Text(language, "正在准备下载…", "Preparing the download…");
      EnsureStallTimer();
      MarkBusyProgress(item.Id);
       Row(item).BusyText = _pluginCatalogBusyText;
      liveRow.BusyDetail = "";
      var catalogDownloadProgress = new Progress<double>(fraction =>
      {
           _pluginCatalogBusyText = AppLocalization.Text(language, "正在下载更新包…", "Downloading the update package…");
           Row(item).BusyText = _pluginCatalogBusyText;
          MarkBusyProgress(item.Id);
          InstallProgress[item.Id] = fraction;
          PackageProgressChanged?.Invoke(item.Id, fraction);
          Row(item).BusyTextBrush = TryFindResource("AccentBrush") as System.Windows.Media.Brush;
          Row(item).BusyBarBrush = Row(item).BusyTextBrush;
          Row(item).BusyIndeterminate = false;
          Row(item).BusyPercent = Math.Clamp(fraction * 100, 0, 100);
          Row(item).BusyDetail = $"{Row(item).BusyPercent:0}%";
      });
      string downloaded;
      try
      {
        downloaded = await _extensionUpdates.DownloadAsync(new ExtensionUpdateRelease
            {
                Id = record.Id,
                Type = record.Type,
                Version = record.Version,
                PackageName = Path.GetFileName(new Uri(record.DownloadUrl).AbsolutePath),
                DownloadUrl = record.DownloadUrl,
                Digest = $"sha256:{record.Sha256}"
            }, CancellationToken.None, catalogDownloadProgress);
          // None, rather than the window's token: closing the settings panel used to cancel an
          // install that was already under way, which is a surprising thing for a button that
          // said "installing" to do. The download is not the window's work -- the window only
          // shows it -- and the parts that follow it (verify, install) are not cancellable
          // either, so a cancel here would leave the package half handled. The UI updates that
          // follow are written to rows that no longer exist if the window is gone; writing to a
          // detached view is harmless, and the work finishes.
      }
      catch (Exception error)
      {
           EndBusy(item.Id, failed: true, error.Message, item);
          AnnounceDownload(item.Record.Name, AppLocalization.Text(language, "下载失败", "Download failed"), error.Message);
          throw;
      }
            try
            {
                _extensionLibrary.ImportPackage(downloaded);
                Row(item).BusyIndeterminate = true;
      Row(item).BusyText = AppLocalization.Text(language, "正在校验完整性…", "Verifying the package…");
      var package = _extensionLibrary.Scan().FirstOrDefault(value => string.Equals(value.Id, record.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Version, record.Version, StringComparison.OrdinalIgnoreCase));
                if (package is null) throw new InvalidDataException("下载的插件清单与目录版本不一致。");
                if (record.Type.Equals("feature", StringComparison.OrdinalIgnoreCase))
                {
                    var installedFeature = _featureExtensions.InstallFeaturePackage(package.PackagePath);
                    StartBackgroundFeatureIfNeeded(installedFeature);
                }
                else if (record.Type.Equals("pet", StringComparison.OrdinalIgnoreCase))
                {
                    _extensions.InstallPetPackage(package.PackagePath);
                    AddInstalledPetStyles();
                    UpdatePetStyleAvailability();
                }
                else if (record.Type.Equals("theme", StringComparison.OrdinalIgnoreCase))
                {
                    var selectedThemeId = SelectedTag(ThemeBox, _settings.ThemeId);
                    _themes.InstallThemePackage(package.PackagePath);
                    RefreshThemeList(selectedThemeId);
                }
                else throw new InvalidDataException("无法识别插件类型。");
            }
            finally
            {
                try { File.Delete(downloaded); } catch (IOException) { }
            }
            RefreshExtensionList();
            RebuildPluginCatalogItems();
            ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            var name = AppLocalization.IsEnglish(language) && !string.IsNullOrWhiteSpace(record.NameEn) ? record.NameEn : record.Name;
            // Cleared here rather than left to the next refresh: the line below says the install
            // finished, and a progress block still claiming to download would contradict it.
            InstallingPackages.Remove(item.Id);
          InstallProgress.Remove(item.Id);
          PackageInstallsChanged?.Invoke();
          Row(item).Busy = false;
            AnnounceDownload(item.Record.Name, AppLocalization.Text(language, "安装完成", "Installed"), "");
      ExtensionMessageText.Text = AppLocalization.Text(language, $"已从插件库安装：{name} v{record.Version}。", $"Installed from the plugin catalog: {name} v{record.Version}.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or FileNotFoundException or NotSupportedException or JsonException or TaskCanceledException)
        {
            if (!_pluginCatalogCancellation.IsCancellationRequested) ShowExtensionError($"插件库安装失败：{error.Message}", $"Plugin catalog installation failed: {error.Message}");
        }
        finally
        {
            _pluginCatalogBusyIds.Remove(item.Id);
            if (!_pluginCatalogCancellation.IsCancellationRequested) RebuildPluginCatalogItems();
        }
    }

    private void OnOpenPluginCatalogRepository(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not PluginCatalogItemView item) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.Record.RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowExtensionError($"打开插件仓库失败：{error.Message}", $"Could not open the plugin repository: {error.Message}");
        }
    }

    private void OnOpenExtensionLibrary(object sender, RoutedEventArgs e)
    {
        try { _extensionLibrary.OpenFolder(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowExtensionError($"打开扩展库失败：{error.Message}", $"Failed to open extension library: {error.Message}"); }
    }

    private void OnInstallExtension(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "BalancePet 扩展 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            Title = "导入 BalancePet 扩展到扩展库"
        };
        if (dialog.ShowDialog(this) != true) return;
        ImportAndInstallPackage(dialog.FileName);
    }

    private void OnExtensionDragOver(object sender, System.Windows.DragEventArgs e)
        => e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;

    private void OnExtensionDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        var zip = paths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(zip)) ImportAndInstallPackage(zip);
    }

    /// <summary>Re-reads the local library after an install finished, whoever started it.</summary>
    private void OnPackageProgressChanged(string id, double fraction)
    {
        if (!IsLoaded) return;
        foreach (var view in PluginCatalogListBox.Items.OfType<PluginCatalogItemView>())
        {
            if (!string.Equals(view.Id, id, StringComparison.Ordinal)) continue;
            view.BusyPercent = System.Math.Clamp(fraction * 100, 0, 100);
            view.BusyDetail = ((int)System.Math.Round(view.BusyPercent)).ToString() + (char)37;
            view.BusyIndeterminate = false;
          // The phase line as well: it lived in the window that started the download, so a row
          // rebuilt here had none -- the number was live while the sentence above it was missing.
          if (string.IsNullOrEmpty(view.BusyText)) view.BusyText = AppLocalization.Text(_settings.Language, "正在下载更新包…", "Downloading the update package…");
            break;
        }
    }
    private void OnPackageInstallsChanged()
    {
        if (!IsLoaded) return;
        RefreshExtensionList();
        // Only rebuilt when the catalogue has actually been read. A window opened a moment ago has
        // no entries yet -- its fetch is still in flight -- and rebuilding then replaced the list
        // with nothing, which is what "0 个扩展" beside "正在读取在线插件目录…" was: the local
        // list is the part that an install can change, and the catalogue rebuild waits its turn.
        // The appearance dropdown is part of this too: a pet package that finished installing has to
        // appear in it without the panel being reopened.
        AddInstalledPetStyles();
        UpdatePetStyleAvailability();
        if (_pluginCatalogEntries.Count > 0) RebuildPluginCatalogItems();
    }

        private readonly System.Windows.Threading.DispatcherTimer _statusFadeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<System.Windows.Controls.TextBlock, (string Text, DateTime Since)> _statusSeen = new();

    /// <summary>
    /// Clears the result lines a few seconds after they were written.
    /// </summary>
    /// <remarks>
    /// These say what just happened -- installed, scanned, saved -- and a line that says what just
    /// happened stops being true the moment something else happens, yet they used to stay on screen
    /// until the next one replaced them. Nothing is hooked up to write them: the timer watches their
    /// text instead, so a line written by any path at all is cleared without that path knowing.
    /// </remarks>
    private void StartStatusFade()
    {
        _statusFadeTimer.Tick += (_, _) =>
        {
            foreach (var block in new[]
            {
                PetStyleMessageText, ExtensionMessageText, ThemeMessageText,
                PluginCatalogStatusText, ExtensionUpdateStatusText, MessageText
            })
            {
                if (block is null) continue;
                var text = block.Text ?? "";
                if (text.Length == 0) { _statusSeen.Remove(block); continue; }
                if (!_statusSeen.TryGetValue(block, out var seen) || !string.Equals(seen.Text, text, StringComparison.Ordinal))
                {
                    _statusSeen[block] = (text, DateTime.UtcNow);
                    continue;
                }
                if ((DateTime.UtcNow - seen.Since).TotalSeconds >= 4) block.Text = "";
            }
        };
        _statusFadeTimer.Start();
    }
private async void ImportAndInstallPackage(string sourcePath)
    {
        // One at a time. Unpacking no longer blocks the UI, so a second drop used to start while
        // the first was still copying into the library -- and the two collided on the same target
        // file, which is what produced a red "being used by another process" followed by a green
        // "installed" for what the reader could only see as one action.
        if (_installingPackage) return;
        _installingPackage = true;
        var importLanguage = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        ExtensionMessageText.Foreground = ThemeBrush("MutedBrush", System.Windows.Media.Brushes.Gray);
        ExtensionMessageText.Text = AppLocalization.Text(importLanguage,
            "正在安装扩展包…大包可能要几秒。", "Installing the package… a large one can take a few seconds.");
        ExtensionInstallBar.Visibility = System.Windows.Visibility.Visible;
        try
        {
            // Unpacking and scanning happen off the UI thread. A feature package is tens of
            // megabytes of archive, and doing that here froze the window until it was done --
            // which is exactly what "the panel hangs for a moment, then says installed" was.
            // Everything after these two awaits is the quick part: the install itself copies an
            // already-unpacked directory into place, and the refreshes below touch the UI and so
            // have to stay on this thread.
            var path = await Task.Run(() => _extensionLibrary.ImportPackage(sourcePath));
            var package = await Task.Run(() => _extensionLibrary.Scan()
                .FirstOrDefault(value => string.Equals(value.PackagePath, path, StringComparison.OrdinalIgnoreCase)));
            if (package is null) throw new InvalidDataException("无法读取扩展包清单。");
            if (package.Type.Equals("feature", StringComparison.OrdinalIgnoreCase))
            {
                var installed = _featureExtensions.InstallFeaturePackage(path);
                StartBackgroundFeatureIfNeeded(installed);
            }
            else if (package.Type.Equals("pet", StringComparison.OrdinalIgnoreCase))
            {
                _extensions.InstallPetPackage(path);
                AddInstalledPetStyles();
                UpdatePetStyleAvailability();
            }
            else if (package.Type.Equals("theme", StringComparison.OrdinalIgnoreCase))
            {
                var selectedThemeId = SelectedTag(ThemeBox, _settings.ThemeId);
                _themes.InstallThemePackage(path);
                RefreshThemeList(selectedThemeId);
            }
            RefreshExtensionList();
            ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            var name = AppLocalization.IsEnglish(language) && !string.IsNullOrWhiteSpace(package.NameEn) ? package.NameEn : package.Name;
            ExtensionMessageText.Text = AppLocalization.Text(language, $"已安装：{name} v{package.Version}。", $"Installed: {name} v{package.Version}.");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FileNotFoundException or NotSupportedException or JsonException)
        { ShowExtensionError($"安装失败：{error.Message}", $"Installation failed: {error.Message}"); }
        ExtensionInstallBar.Visibility = System.Windows.Visibility.Collapsed;
        _installingPackage = false;
    }

    private void OnCleanupExtensionResources(object sender, RoutedEventArgs e)
    {
        var removed = _extensionLibrary.CleanupOldPackages();
        var roots = new[] { _extensions.RootDirectory, _featureExtensions.RootDirectory, _themes.RootDirectory };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var idDirectory in Directory.EnumerateDirectories(root))
            {
                if (Path.GetFileName(idDirectory).Equals(".staging", StringComparison.OrdinalIgnoreCase)) continue;
                var versions = Directory.EnumerateDirectories(idDirectory)
                    .Where(path => !Path.GetFileName(path).Equals(".staging", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(path => ParseExtensionVersion(Path.GetFileName(path))).ToArray();
                foreach (var old in versions.Skip(1))
                {
                    try { Directory.Delete(old, true); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }
        RefreshExtensionList();
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
        ExtensionMessageText.Text = AppLocalization.Text(language, $"旧版资源清理完成，删除 {removed} 个旧版本。", $"Removed {removed} old extension version(s).");
    }

    private static Version ParseExtensionVersion(string value)
    {
        var numeric = value.Split('-', '+')[0];
        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0, 0);
    }

    private void OnInstallSelectedExtension(object sender, RoutedEventArgs e)
    {
        if (GetExtensionEntry(sender) is not ExtensionCatalogEntry selected || selected.Package is null) return;
        try
        {
            if (selected.Package.Type.Equals("feature", StringComparison.OrdinalIgnoreCase))
            {
                var installedFeature = _featureExtensions.InstallFeaturePackage(selected.Package.PackagePath);
                StartBackgroundFeatureIfNeeded(installedFeature);
            }
            else if (selected.Package.Type.Equals("pet", StringComparison.OrdinalIgnoreCase))
            {
                _extensions.InstallPetPackage(selected.Package.PackagePath);
                AddInstalledPetStyles();
                UpdatePetStyleAvailability();
            }
            else if (selected.Package.Type.Equals("theme", StringComparison.OrdinalIgnoreCase))
            {
                var selectedThemeId = SelectedTag(ThemeBox, _settings.ThemeId);
                _themes.InstallThemePackage(selected.Package.PackagePath);
                RefreshThemeList(selectedThemeId);
            }
            else throw new InvalidDataException("无法识别扩展类型。");
            RefreshExtensionList();
            ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            var packageName = AppLocalization.IsEnglish(language) && !string.IsNullOrWhiteSpace(selected.Package.NameEn)
                ? selected.Package.NameEn
                : selected.Package.Name;
            ExtensionMessageText.Text = AppLocalization.Text(language, $"已安装：{packageName} v{selected.Package.Version}。", $"Installed: {packageName} v{selected.Package.Version}.");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FileNotFoundException or NotSupportedException or JsonException)
        { ShowExtensionError($"安装失败：{error.Message}", $"Installation failed: {error.Message}"); }
    }

    private void OnToggleExtension(object sender, RoutedEventArgs e)
    {
        if (GetExtensionEntry(sender) is not ExtensionCatalogEntry selected || !selected.IsInstalled) return;
        if (selected.Theme is not null)
        {
            ShowExtensionError("主题安装后会自动出现在“外观 > 当前主题”中，无需单独启用。", "Installed themes appear under Appearance > Current theme and do not need to be enabled separately.");
            return;
        }
        var enabled = !selected.IsEnabled;
        var changed = selected.Pet is not null
            ? _extensions.SetEnabled(selected.Pet.Manifest.Id, enabled)
            : selected.Feature is not null && _featureExtensions.SetEnabled(selected.Feature.Manifest.Id, enabled);
        if (!changed) return;
        if (enabled && selected.Feature is not null)
            StartBackgroundFeatureIfNeeded(selected.Feature);
        if (selected.Pet is PetExtensionInfo petSelected && !enabled && string.Equals(SelectedTag(PetStyleBox, PetStyleCatalog.FallbackId), petSelected.StyleId, StringComparison.OrdinalIgnoreCase))
            SelectFirstAvailablePetStyle();
        if (selected.Pet is not null)
        {
            AddInstalledPetStyles();
            UpdatePetStyleAvailability();
        }
        RefreshExtensionList();
        ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        ExtensionMessageText.Text = enabled
            ? AppLocalization.Text(language, "扩展已启用。", "Extension enabled.")
            : AppLocalization.Text(language, "扩展已禁用；已使用它的形象会回退到 DeepSeek。", "Extension disabled; appearances using it fall back to DeepSeek.");
    }

    private void OnLaunchExtension(object sender, RoutedEventArgs e)
    {
        if (GetExtensionEntry(sender) is not ExtensionCatalogEntry entry || entry.Feature is not FeatureExtensionInfo selected) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        if (_featureExtensions.TryLaunch(selected.Manifest.Id, out var error))
        {
            ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            ExtensionMessageText.Text = AppLocalization.Text(language, "功能扩展已启动。", "Feature extension launched.");
        }
        else
        {
            ExtensionMessageText.Foreground = ThemeBrush("DangerBrush", System.Windows.Media.Brushes.Firebrick);
            ExtensionMessageText.Text = AppLocalization.Text(language, $"启动失败：{error}", $"Launch failed: {error}");
        }
        RefreshExtensionList();
    }

    private void StartBackgroundFeatureIfNeeded(FeatureExtensionInfo feature)
    {
        if (feature.Manifest.Capabilities.Contains("notifications.present", StringComparer.Ordinal))
            _featureExtensions.TryLaunch(feature.Manifest.Id, out _, background: true);
    }

    private void OnUninstallExtension(object sender, RoutedEventArgs e)
    {
        if (GetExtensionEntry(sender) is not ExtensionCatalogEntry selected || !selected.IsInstalled) return;
        if (selected.Theme is not null && string.Equals(selected.Theme.Manifest.Id, ThemeExtensionManager.BundledThemeId, StringComparison.OrdinalIgnoreCase))
        {
            ShowExtensionError("内置云母主题是主题系统的安全回退，不能卸载。", "The bundled Mica theme is the theme system fallback and cannot be uninstalled.");
            return;
        }
        // Same reasoning as the theme guard above: the pet is the whole window, so
        // removing the last appearance would leave a blank window and an empty shape
        // selector, with no way back through the interface.
        if (selected.Pet is not null && PetStyleCatalog.IsLastAvailableStyle(selected.Pet.StyleId))
        {
            ShowExtensionError(
                "这是最后一套可用形象，不能卸载。请先安装或启用另一套形象。",
                "This is the last available appearance and cannot be uninstalled. Install or enable another one first.");
            return;
        }
        var selectedName = selected.Pet?.Manifest.Name ?? selected.Feature?.Manifest.Name ?? selected.Theme?.Manifest.Name ?? selected.Id;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var answer = System.Windows.MessageBox.Show(this,
            AppLocalization.Text(language, $"确定卸载扩展“{selectedName}”吗？这只会删除它的扩展目录，不会影响主程序和其他扩展。", $"Uninstall extension \"{selectedName}\"? Only its extension directory will be removed; the main program and other extensions are unaffected."),
            AppLocalization.Text(language, "卸载扩展", "Uninstall extension"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var removed = selected.Pet is not null
            ? _extensions.Uninstall(selected.Pet.Manifest.Id)
            : selected.Feature is not null
                ? _featureExtensions.Uninstall(selected.Feature.Manifest.Id)
                : selected.Theme is not null && _themes.Uninstall(selected.Theme.Manifest.Id);
        if (!removed) return;
        if (selected.Pet is PetExtensionInfo petSelected && string.Equals(SelectedTag(PetStyleBox, PetStyleCatalog.FallbackId), petSelected.StyleId, StringComparison.OrdinalIgnoreCase))
            SelectFirstAvailablePetStyle();
        if (selected.Pet is not null)
        {
            AddInstalledPetStyles();
            UpdatePetStyleAvailability();
        }
        if (selected.Theme is not null)
        {
            var removedSelectedTheme = string.Equals(SelectedTag(ThemeBox, _settings.ThemeId), selected.Theme.Manifest.Id, StringComparison.OrdinalIgnoreCase);
            _themes.EnsureBundledThemeInstalled();
            RefreshThemeList(removedSelectedTheme ? ThemeExtensionManager.BundledThemeId : SelectedTag(ThemeBox, _settings.ThemeId));
            if (removedSelectedTheme)
            {
                ApplySelectedTheme();
                MarkSettingsDirty();
            }
        }
        RefreshExtensionList();
        ExtensionMessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
        ExtensionMessageText.Text = AppLocalization.Text(language, "扩展已卸载；扩展库中的 ZIP 仍然保留。点击“扫描扩展库”可再次安装。", "Extension uninstalled; the ZIP in the library was kept. Scan the library to install it again.");
    }

    private ExtensionCatalogEntry? GetExtensionEntry(object sender)
        => (sender as FrameworkElement)?.DataContext as ExtensionCatalogEntry ?? ExtensionListBox.SelectedItem as ExtensionCatalogEntry;

    private void ShowExtensionError(string chinese, string english)
    {
        ExtensionMessageText.Foreground = ThemeBrush("DangerBrush", System.Windows.Media.Brushes.Firebrick);
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        ExtensionMessageText.Text = AppLocalization.Text(language, chinese, english);
    }

    private static MonitorProfile CreateProfileFromLegacy(PetSettings settings) => new()
    {
        Id = "default",
        Name = "默认账户",
        PresetId = BalancePresetCatalog.Custom,
        Endpoint = settings.Endpoint,
        AuthMode = settings.AuthMode,
        HeaderName = settings.HeaderName,
        TokenBlob = settings.TokenBlob,
        BalancePath = settings.BalancePath,
        Currency = settings.Currency,
        RefreshSeconds = Math.Max(30, settings.RefreshSeconds),
        AutoRefreshEnabled = settings.AutoRefreshEnabled,
        LowThreshold = settings.LowThreshold,
        Enabled = true
    };

    private static MonitorProfile CloneProfile(MonitorProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        PresetId = profile.PresetId,
        SiteUrl = profile.SiteUrl,
        Endpoint = profile.Endpoint,
        UsageDetailEndpoint = profile.UsageDetailEndpoint,
        AuthMode = profile.AuthMode,
        HeaderName = profile.HeaderName,
        TokenBlob = profile.TokenBlob,
        WebSessionBlob = profile.WebSessionBlob,
        BrowserSessionBrowser = profile.BrowserSessionBrowser,
        BalancePath = profile.BalancePath,
        Currency = profile.Currency,
        RefreshSeconds = profile.RefreshSeconds,
        AutoRefreshEnabled = profile.AutoRefreshEnabled,
        LowThreshold = profile.LowThreshold,
        Enabled = profile.Enabled,
        IsSubscription = profile.IsSubscription
    };

    private MonitorProfile? CurrentProfile => _profiles.FirstOrDefault(profile => string.Equals(profile.Id, _currentProfileId, StringComparison.OrdinalIgnoreCase));

    private void RefreshProfileList(string? selectedId)
    {
        _suppressProfileChange = true;
        ProfileBox.Items.Clear();
        foreach (var profile in _profiles)
            ProfileBox.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });
        var selectedIndex = _profiles.FindIndex(profile => string.Equals(profile.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        ProfileBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        _suppressProfileChange = false;
        if (ProfileBox.SelectedItem is ComboBoxItem item) LoadProfile(item.Tag?.ToString() ?? "");
        UpdateAccountSummary();
    }

    private void LoadProfile(string profileId)
    {
        var profile = _profiles.FirstOrDefault(value => string.Equals(value.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return;
        _currentProfileId = profile.Id;
        ProfileNameBox.Text = profile.Name;
        _suppressPresetChange = true;
        SelectByTag(PresetBox, BalancePresetCatalog.NormalizeId(profile.PresetId));
        _suppressPresetChange = false;
        _suppressSiteUrlChange = true;
        SiteUrlBox.Text = BalancePresetCatalog.UsesSiteUrl(profile.PresetId) ? BalancePresetCatalog.ResolveSiteUrl(profile) : "";
        _suppressSiteUrlChange = false;
        EndpointBox.Text = profile.Endpoint;
        UsageDetailEndpointBox.Text = profile.UsageDetailEndpoint;
        HeaderBox.Text = profile.HeaderName;
        PathBox.Text = profile.BalancePath;
        CurrencyBox.Text = profile.Currency;
        _suppressRefreshChange = true;
        var refreshTag = !profile.AutoRefreshEnabled ? "off" : profile.RefreshSeconds is 30 or 60 or 300 or 900 or 1800 or 3600 ? profile.RefreshSeconds.ToString(CultureInfo.InvariantCulture) : "custom";
        SelectByTag(RefreshBox, refreshTag);
        RefreshCustomBox.Text = Math.Max(30, profile.RefreshSeconds).ToString(CultureInfo.InvariantCulture);
        _suppressRefreshChange = false;
        UpdateRefreshModeVisibility();
        ThresholdBox.Text = profile.LowThreshold.ToString(CultureInfo.InvariantCulture);
        SelectByTag(AuthModeBox, profile.AuthMode);
        MonitorEnabledBox.IsChecked = profile.Enabled;
        SubscriptionBox.IsChecked = profile.IsSubscription;
        TokenBox.Clear();
        WebSessionBox.Clear();
        SelectByTag(BrowserSessionBrowserBox, string.Equals(profile.BrowserSessionBrowser, "chrome", StringComparison.OrdinalIgnoreCase) ? "chrome" : "edge");
        if (!string.IsNullOrWhiteSpace(profile.WebSessionBlob))
        {
            BrowserPairingCodeText.Text = "已保存";
            BrowserSessionStatusText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            BrowserSessionStatusText.Text = "网页会话已加密保存。点击“应用”或“确定”后，刷新用量记录即可验证费用明细接口。";
        }
        else
        {
            BrowserPairingCodeText.Text = "未连接";
            BrowserSessionStatusText.Foreground = ThemeBrush("MutedBrush", System.Windows.Media.Brushes.Gray);
            BrowserSessionStatusText.Text = "";
        }
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        UpdatePresetUi(true);
        UpdateAccountSummary();
    }

    private void UpdateAccountSummary()
    {
        if (AccountSummaryNameText is null || AccountSummaryHintText is null || AccountSummaryStatusText is null) return;
        var profile = CurrentProfile;
        if (profile is null) return;

        AccountSummaryNameText.Text = string.IsNullOrWhiteSpace(profile.Name) ? "监控账户" : profile.Name;
        AccountSummaryStatusText.Text = profile.Enabled ? "已启用" : "已停用";
        AccountSummaryStatusText.Foreground = profile.Enabled
            ? ThemeBrush("AccentBrush", System.Windows.Media.Brushes.DarkSlateBlue)
            : ThemeBrush("MutedBrush", System.Windows.Media.Brushes.Gray);

        var address = BalancePresetCatalog.UsesSiteUrl(profile.PresetId) ? profile.SiteUrl : profile.Endpoint;
        var endpointHint = Uri.TryCreate(address, UriKind.Absolute, out var endpoint) && !string.IsNullOrWhiteSpace(endpoint.Host)
            ? endpoint.Host
            : "余额接口尚未配置";
        AccountSummaryHintText.Text = endpointHint;
    }

    private void OnMonitorEnabledChanged(object sender, RoutedEventArgs e)
    {
        MarkSettingsDirty();
        UpdateAccountSummary();
    }


    private readonly System.Windows.Media.MediaPlayer _volumePreview = new();

    private void OnVolumePreviewClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "press.mp3");
            if (!File.Exists(path)) return;
            // Played regardless of the 按压音效 switch and at whatever the slider
            // shows, so the volume can be judged before saving and without closing
            // the panel just to click the pet.
            _volumePreview.Stop();
            _volumePreview.Open(new Uri(path));
            _volumePreview.Volume = Math.Clamp(VolumeSlider.Value, 0, 1);
            _volumePreview.Play();
        }
        catch (InvalidOperationException) { }
        catch (IOException) { }
    }

    private void MarkSettingsDirty()
    {
        if (_trackChanges && !_suppressChangeTracking) _hasUnsavedChanges = true;
    }

    private void OnSettingTextChanged(object sender, TextChangedEventArgs e) => MarkSettingsDirty();

    private void OnSettingPasswordChanged(object sender, RoutedEventArgs e) => MarkSettingsDirty();

    private void OnSettingValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => MarkSettingsDirty();

    private void OnSettingToggleChanged(object sender, RoutedEventArgs e)
    {
        MarkSettingsDirty();
        if (ReferenceEquals(sender, NavigationAnimationsBox) && NavigationAnimationsBox.IsChecked != true && IsLoaded)
            ApplyNavigationState();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // Closing discards whatever was not applied. The overlay that asked first is gone: it
        // appeared for changes that had been undone again, and for things that are not settings at
        // all -- uninstalling an appearance, for one -- so it asked a question whose answer did not
        // depend on the question. Apply and OK still save, and everything else is discarded.
        _ = _allowCloseWithoutPrompt;
        // Read so the flag keeps a reader: it is still maintained by every change handler, and
        // deleting that bookkeeping is a bigger change than removing the question it was for.
        _ = _hasUnsavedChanges;
    }

    private void ShowUnsavedChangesOverlay()
    {
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        UnsavedDialogTitleText.Text = AppLocalization.Text(language, "未保存的设置", "Unsaved settings");
        UnsavedDialogMessageText.Text = AppLocalization.Text(language, "设置尚未保存，确定放弃更改并关闭吗？", "Settings have not been saved. Discard changes and close?");
        UnsavedKeepEditingButton.Content = AppLocalization.Text(language, "继续编辑", "Keep editing");
        UnsavedDiscardButton.Content = AppLocalization.Text(language, "放弃更改", "Discard changes");
        UnsavedOverlay.Visibility = Visibility.Visible;
        UnsavedKeepEditingButton.Focus();
    }

    private void OnKeepEditingClick(object sender, RoutedEventArgs e)
    {
        UnsavedOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDiscardChangesClick(object sender, RoutedEventArgs e)
    {
        _hasUnsavedChanges = false;
        _allowCloseWithoutPrompt = true;
        UnsavedOverlay.Visibility = Visibility.Collapsed;
        Close();
    }

    private void SaveCurrentProfileFields()
    {
        var profile = CurrentProfile;
        if (profile is null) return;
        profile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "监控账户" : ProfileNameBox.Text.Trim();
        var presetId = SelectedTag(PresetBox, BalancePresetCatalog.Custom);
        if (BalancePresetCatalog.UsesSiteUrl(presetId))
        {
            BalancePresetCatalog.Apply(profile, presetId, SiteUrlBox.Text);
        }
        else
        {
            profile.PresetId = BalancePresetCatalog.Custom;
            profile.Endpoint = EndpointBox.Text.Trim();
            profile.AuthMode = SelectedTag(AuthModeBox, "bearer");
            profile.HeaderName = HeaderBox.Text.Trim();
            profile.BalancePath = PathBox.Text.Trim();
        }
        profile.UsageDetailEndpoint = UsageDetailEndpointBox.Text.Trim();
        // A JSON path must keep its original casing, because property names are
        // case-sensitive: upper-casing balance_infos.0.currency turns it into a path
        // that resolves to nothing. Only a literal currency label is normalised.
        var currencyText = CurrencyBox.Text.Trim();
        profile.Currency = currencyText.Length == 0
            ? "USD"
            : currencyText.Contains('.', StringComparison.Ordinal) ? currencyText : currencyText.ToUpperInvariant();
        var refreshTag = SelectedTag(RefreshBox, "off");
        profile.AutoRefreshEnabled = !string.Equals(refreshTag, "off", StringComparison.OrdinalIgnoreCase);
        if (profile.AutoRefreshEnabled)
        {
            var refreshText = string.Equals(refreshTag, "custom", StringComparison.OrdinalIgnoreCase)
                ? RefreshCustomBox.Text
                : refreshTag;
            if (int.TryParse(refreshText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var refresh)) profile.RefreshSeconds = Math.Max(30, refresh);
        }
        if (double.TryParse(ThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)) profile.LowThreshold = threshold;
        profile.Enabled = MonitorEnabledBox.IsChecked == true;
        profile.IsSubscription = SubscriptionBox.IsChecked == true;
        if (!string.IsNullOrWhiteSpace(TokenBox.Password)) profile.TokenBlob = _tokens.Protect(TokenBox.Password);
        if (!string.IsNullOrWhiteSpace(WebSessionBox.Password)) profile.WebSessionBlob = _tokens.Protect(WebSessionBox.Password);
        profile.BrowserSessionBrowser = SelectedTag(BrowserSessionBrowserBox, profile.BrowserSessionBrowser).Equals("chrome", StringComparison.OrdinalIgnoreCase) ? "chrome" : "edge";
    }

    private void OnBrowserSessionBrowserChanged(object sender, SelectionChangedEventArgs e)
    {
        // This combo has its own handler rather than OnComboSelectionChanged, so
        // it must refresh its display itself: the custom template does not pick
        // up a new SelectedItem on its own, and without this the box keeps
        // showing the previously chosen browser until the window is reopened.
        SyncComboDisplay(BrowserSessionBrowserBox);
        if (!_suppressProfileChange && _trackChanges) MarkSettingsDirty();
    }

    private void OnProfileNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressProfileChange || ProfileBox is null || ProfileBox.SelectedItem is not ComboBoxItem item) return;
        MarkSettingsDirty();
        item.Content = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "监控账户" : ProfileNameBox.Text.Trim();
        UpdateAccountSummary();
    }

    private void OnRefreshModeChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncComboDisplay(RefreshBox);
        if (_suppressRefreshChange) return;
        MarkSettingsDirty();
        UpdateRefreshModeVisibility();
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncComboDisplay(PresetBox);
        if (_suppressPresetChange || PresetBox is null || SiteUrlBox is null) return;
        MarkSettingsDirty();
        var presetId = SelectedTag(PresetBox, BalancePresetCatalog.Custom);
        if (BalancePresetCatalog.UsesSiteUrl(presetId) && string.IsNullOrWhiteSpace(SiteUrlBox.Text))
        {
            _suppressSiteUrlChange = true;
            SiteUrlBox.Text = BalancePresetCatalog.NormalizeSiteUrl(EndpointBox.Text);
            _suppressSiteUrlChange = false;
        }
        UpdatePresetUi(true);
    }

    private void OnSiteUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSiteUrlChange || PresetBox is null) return;
        MarkSettingsDirty();
        UpdatePresetPreview();
    }

    private void UpdatePresetUi(bool updatePreview)
    {
        if (PresetBox is null || SiteUrlBox is null || EndpointBox is null || AuthModeBox is null || PathBox is null) return;
        var presetId = SelectedTag(PresetBox, BalancePresetCatalog.Custom);
        var usesSiteUrl = BalancePresetCatalog.UsesSiteUrl(presetId);
        var fixedEndpoint = BalancePresetCatalog.HasFixedEndpoint(presetId);
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        RevealElement(SiteUrlLabel, usesSiteUrl);
        RevealElement(SiteUrlBox, usesSiteUrl);
        RevealElement(SiteUrlHint, usesSiteUrl);
        EndpointBox.IsReadOnly = usesSiteUrl || fixedEndpoint;
        AuthModeBox.IsEnabled = !usesSiteUrl;
        PathBox.IsReadOnly = usesSiteUrl || fixedEndpoint;
        // The auto-detecting presets write a human hint into the path box. A
        // manual endpoint must not submit that hint as a real JSON path: an
        // account saved that way fails every refresh with
        // "JSON path not found: 自动识别".
        if (!usesSiteUrl && IsGeneratedBalancePath(PathBox.Text)) PathBox.Text = "";
        EndpointHint.Text = AppLocalization.Text(language,
            usesSiteUrl ? "接口地址由预设自动生成；切换到“自定义接口”后可以手动修改。" : "填写中转站文档中的余额查询 URL，不是网站首页或聊天接口。",
            usesSiteUrl ? "The endpoint is generated by the preset. Switch to Custom endpoint to edit it." : "Enter the balance URL from your relay provider's documentation, not the website or chat endpoint.");
        PresetHint.Text = presetId switch
        {
            BalancePresetCatalog.Auto => AppLocalization.Text(language, "依次尝试同一站点的 /v1/usage 和 /api/usage/token，只执行只读查询。", "Tries /v1/usage and /api/usage/token on the same site using read-only requests."),
            BalancePresetCatalog.V1Usage => AppLocalization.Text(language, "适用于提供 /v1/usage 的中转站；自动识别 balance、remaining 和单位。", "For relays exposing /v1/usage; balance, remaining, and units are detected automatically."),
            BalancePresetCatalog.NewApiToken => AppLocalization.Text(language, "适用于标准 New API 令牌额度接口，并按站点公开的额度与货币设置换算。", "Uses the standard New API token-usage endpoint and the site's published quota and currency settings."),
            BalancePresetCatalog.DeepSeek => AppLocalization.Text(language, "DeepSeek 官方平台的账户余额。只填 API Key（platform.deepseek.com 创建），接口与 JSON 路径已预设。", "The DeepSeek platform account balance. Supply only an API key created on platform.deepseek.com; the endpoint and JSON path are preset."),
            _ => AppLocalization.Text(language, "手动填写完整接口、认证方式和 JSON 路径。", "Enter the full endpoint, authentication method, and JSON path manually.")
        };
        if (updatePreview && (usesSiteUrl || fixedEndpoint)) UpdatePresetPreview();
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
    }

    /// <summary>
    /// True when the path box holds a hint generated for one of the auto-detecting
    /// presets rather than a real JSON path. Matching both languages directly
    /// keeps this independent of the current UI language.
    /// </summary>
    private static bool IsGeneratedBalancePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim() is "自动识别" or "Automatic detection"
            or "balance / remaining（自动）" or "balance / remaining (automatic)"
            or "data.total_available（自动换算）" or "data.total_available (automatic conversion)";
    }

    private void UpdatePresetPreview()
    {
        var presetId = SelectedTag(PresetBox, BalancePresetCatalog.Custom);
        if (BalancePresetCatalog.HasFixedEndpoint(presetId))
        {
            // Everything here is fixed, so fill it in and leave the fields read-only:
            // the account only needs an API key from the user.
            EndpointBox.Text = BalancePresetCatalog.FixedEndpoint(presetId);
            SelectByTag(AuthModeBox, "bearer");
            HeaderBox.Text = "Authorization";
            PathBox.Text = BalancePresetCatalog.FixedBalancePath(presetId);
            CurrencyBox.Text = BalancePresetCatalog.FixedCurrency(presetId);
            return;
        }
        if (!BalancePresetCatalog.UsesSiteUrl(presetId)) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        EndpointBox.Text = BalancePresetCatalog.BuildEndpoint(SiteUrlBox.Text, presetId);
        SelectByTag(AuthModeBox, "bearer");
        HeaderBox.Text = "Authorization";
        PathBox.Text = presetId switch
        {
            BalancePresetCatalog.V1Usage => AppLocalization.Text(language, "balance / remaining（自动）", "balance / remaining (automatic)"),
            BalancePresetCatalog.NewApiToken => AppLocalization.Text(language, "data.total_available（自动换算）", "data.total_available (automatic conversion)"),
            _ => AppLocalization.Text(language, "自动识别", "Automatic detection")
        };
    }

    private void UpdateRefreshModeVisibility()
    {
        var custom = string.Equals(SelectedTag(RefreshBox, "off"), "custom", StringComparison.OrdinalIgnoreCase);
        RevealElement(RefreshCustomBox, custom);
    }

    private bool ValidateRefreshInput()
    {
        var tag = SelectedTag(RefreshBox, "off");
        if (string.Equals(tag, "off", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(tag, "custom", StringComparison.OrdinalIgnoreCase)
            && (!int.TryParse(RefreshCustomBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < 30))
        {
            MessageText.Text = "自定义自动刷新间隔必须是大于等于 30 的整数秒。";
            RefreshCustomBox.Focus();
            return false;
        }
        return true;
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncComboDisplay(ProfileBox);
        if (_suppressProfileChange || ProfileBox.SelectedItem is not ComboBoxItem item) return;
        SaveCurrentProfileFields();
        _suppressChangeTracking = true;
        try { LoadProfile(item.Tag?.ToString() ?? ""); }
        finally { _suppressChangeTracking = false; }
    }

    private void OnAddProfile(object sender, RoutedEventArgs e)
    {
        MarkSettingsDirty();
        SaveCurrentProfileFields();
        var profile = new MonitorProfile { Id = Guid.NewGuid().ToString("N"), Name = $"监控账户 {_profiles.Count + 1}", PresetId = BalancePresetCatalog.Auto, Endpoint = "", Enabled = true };
        _profiles.Add(profile);
        RefreshProfileList(profile.Id);
        SiteUrlBox.Focus();
    }

    private void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1) { MessageText.Text = "至少保留一个监控账户。"; return; }
        MarkSettingsDirty();
        var profile = CurrentProfile;
        if (profile is null) return;
        var index = _profiles.IndexOf(profile);
        _profiles.Remove(profile);
        RefreshProfileList(_profiles[Math.Clamp(index - 1, 0, _profiles.Count - 1)].Id);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "BalancePet 设置 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "导入 BalancePet 设置" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(dialog.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported is null) throw new InvalidDataException("设置文件为空或格式不正确。");
            SaveCurrentProfileFields();
            MarkSettingsDirty();
            _profiles.Clear();
            if (imported.Monitors is { Count: > 0 }) _profiles.AddRange(imported.Monitors.Select(CloneProfile));
            else _profiles.Add(CreateProfileFromLegacy(imported));
            RefreshProfileList(imported.SelectedMonitorId);
            SelectByTag(PetStyleBox, imported.PetStyle); SelectByTag(InteractionBox, imported.InteractionMode); SelectByTag(UpdateCheckBox, imported.UpdateCheckMode); SelectByTag(ExtensionUpdateCheckBox, imported.ExtensionUpdateCheckMode);
            SelectByTag(LanguageBox, imported.Language);
            RefreshThemeList(imported.ThemeId);
            SelectByTag(ThemeModeBox, imported.ThemeMode);
            SelectByTag(ThemeBackdropBox, imported.ThemeBackdrop);
            _selectedThemeMode = SelectedTag(ThemeModeBox, "system");
            _selectedThemeBackdrop = SelectedTag(ThemeBackdropBox, "mica");
            _selectedThemeId = SelectedTag(ThemeBox, ThemeExtensionManager.BundledThemeId);
                ApplySelectedTheme();
            ScaleSlider.Value = Math.Clamp(imported.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(imported.Volume, 0, 1);
 SoundBox.IsChecked = imported.Sound; BubbleBox.IsChecked = imported.Bubble; NoticeBubbleBox.IsChecked = imported.NoticesNotify; InteractionEffectsBox.IsChecked = imported.InteractionEffects; NavigationAnimationsBox.IsChecked = imported.NavigationAnimations; EasterEggsBox.IsChecked = imported.RandomEasterEggs; FollowCodexBox.IsChecked = imported.CodexTaskIntegration; FollowDeepSeekHarnessBox.IsChecked = imported.DeepSeekHarnessIntegration; FollowGeminiBox.IsChecked = imported.GeminiTaskIntegration; FollowQwenBox.IsChecked = imported.QwenTaskIntegration; FollowClaudeBox.IsChecked = imported.ClaudeTaskIntegration; FollowOtherBox.IsChecked = imported.OtherTaskIntegration; StartupBox.IsChecked = imported.StartWithWindows;
            OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
            AppLocalization.Apply(this, imported.Language);
            RefreshLanguageSelector(imported.Language, selectLanguage: false);
            TokenBox.Clear();
            MessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            MessageText.Text = "设置已导入。令牌不会从文件导入，请在各监控账户中重新填写令牌。";
        }
        catch (Exception error)
        {
            MessageText.Foreground = ThemeBrush("DangerBrush", System.Windows.Media.Brushes.Firebrick);
            MessageText.Text = $"导入失败：{error.Message}";
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "BalancePet 设置 (*.json)|*.json", DefaultExt = ".json", FileName = "balance-pet-settings.json", Title = "导出 BalancePet 设置" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SaveCurrentProfileFields();
            var selected = CurrentProfile ?? _profiles[0];
            var export = new
            {
                endpoint = selected.Endpoint,
                language = SelectedTag(LanguageBox, "zh-CN"),
                theme_id = SelectedTag(ThemeBox, ThemeExtensionManager.BundledThemeId),
                theme_mode = _selectedThemeMode,
                theme_backdrop = _selectedThemeBackdrop,
                auth_mode = selected.AuthMode,
                header_name = selected.HeaderName,
                balance_path = selected.BalancePath,
                currency = selected.Currency,
                refresh_seconds = selected.RefreshSeconds,
                auto_refresh_enabled = selected.AutoRefreshEnabled,
                low_threshold = selected.LowThreshold,
                pet_style = SelectedTag(PetStyleBox, PetStyleCatalog.FallbackId),
                interaction_mode = SelectedTag(InteractionBox, "free"),
                update_check_mode = SelectionTag(UpdateCheckBox, "daily"),
                extension_update_check_mode = SelectionTag(ExtensionUpdateCheckBox, "daily"),
                pet_scale = ScaleSlider.Value,
                sound = SoundBox.IsChecked == true,
                volume = VolumeSlider.Value,
                bubble = BubbleBox.IsChecked == true,
            noticesNotify = NoticeBubbleBox.IsChecked == true,
                interaction_effects = InteractionEffectsBox.IsChecked == true,
                navigation_animations = NavigationAnimationsBox.IsChecked == true,
                random_easter_eggs = EasterEggsBox.IsChecked == true,
                codex_task_integration = FollowCodexBox.IsChecked == true,
                deepseek_harness_integration = FollowDeepSeekHarnessBox.IsChecked == true,
                gemini_task_integration = FollowGeminiBox.IsChecked == true,
                qwen_task_integration = FollowQwenBox.IsChecked == true,
                claude_task_integration = FollowClaudeBox.IsChecked == true,
                other_task_integration = FollowOtherBox.IsChecked == true,
                start_with_windows = StartupBox.IsChecked == true,
                selected_monitor_id = selected.Id,
                monitors = _profiles.Select(profile => new
                {
                    id = profile.Id,
                    name = profile.Name,
                    preset_id = profile.PresetId,
                     site_url = profile.SiteUrl,
                     endpoint = profile.Endpoint,
                     usage_detail_endpoint = profile.UsageDetailEndpoint,
                     auth_mode = profile.AuthMode,
                    header_name = profile.HeaderName,
                    balance_path = profile.BalancePath,
                    currency = profile.Currency,
                    refresh_seconds = Math.Max(30, profile.RefreshSeconds),
                    auto_refresh_enabled = profile.AutoRefreshEnabled,
                    low_threshold = profile.LowThreshold,
                    enabled = profile.Enabled,
                    is_subscription = profile.IsSubscription,
                }).ToArray()
            };
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, options));
            MessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            MessageText.Text = "设置已导出。文件中不包含访问令牌。";
        }
        catch (Exception error)
        {
            MessageText.Foreground = ThemeBrush("DangerBrush", System.Windows.Media.Brushes.Firebrick);
            MessageText.Text = $"导出失败：{error.Message}";
        }
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        await SaveSettingsAndMaybeTestAsync(testConnection: false, closeAfterSave: true);
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        await SaveSettingsAndMaybeTestAsync(testConnection: false, closeAfterSave: false);
    }

    private async Task SaveSettingsAndMaybeTestAsync(bool testConnection, bool closeAfterSave)
    {
        SaveCurrentProfileFields();
        if (_profiles.Count == 0) { MessageText.Text = "至少保留一个监控账户。"; return; }
        if (!ValidateRefreshInput()) return;
        foreach (var profile in _profiles.Where(value => value.Enabled))
        {
            var address = BalancePresetCatalog.UsesSiteUrl(profile.PresetId) ? profile.SiteUrl : profile.Endpoint;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            {
                MessageText.Text = $"监控账户“{profile.Name}”的{(BalancePresetCatalog.UsesSiteUrl(profile.PresetId) ? "中转站地址" : "接口地址")}无效。";
                RefreshProfileList(profile.Id);
                return;
            }
            if (!BalancePresetCatalog.UsesSiteUrl(profile.PresetId) && (string.IsNullOrWhiteSpace(profile.BalancePath) || IsGeneratedBalancePath(profile.BalancePath))) { MessageText.Text = $"监控账户“{profile.Name}”的余额 JSON 路径不能为空，也不能是预设的提示文字。"; RefreshProfileList(profile.Id); return; }
            if (!BalancePresetCatalog.UsesSiteUrl(profile.PresetId) && profile.AuthMode == "custom" && string.IsNullOrWhiteSpace(profile.HeaderName)) { MessageText.Text = $"监控账户“{profile.Name}”使用自定义 Header 时必须填写 Header 名。"; RefreshProfileList(profile.Id); return; }
        }
        var selected = CurrentProfile ?? _profiles[0];
        var selectedToken = "";
        if (testConnection)
        {
            try
            {
                selectedToken = _tokens.Unprotect(selected.TokenBlob);
            }
            catch (Exception error)
            {
                MessageText.Text = $"当前账户令牌无法解密：{error.Message}";
                return;
            }
        }
        try
        {
            var startupEnabled = StartupBox.IsChecked == true;
            var selectedPetStyle = SelectedTag(PetStyleBox, PetStyleCatalog.FallbackId);
            if (!PetStyleCatalog.IsAvailable(selectedPetStyle)) selectedPetStyle = PetStyleCatalog.FallbackId;
            var updated = new PetSettings
            {
                // Preserve the current schema so SettingsStore does not treat
                // this newly assembled snapshot as a legacy file and reset the
                // theme settings during normalization.
                SettingsSchemaVersion = _settings.SettingsSchemaVersion,
                Endpoint = selected.Endpoint,
                Language = SelectedTag(LanguageBox, "zh-CN"),
                ThemeId = _selectedThemeId,
                ThemeMode = _selectedThemeMode,
                ThemeBackdrop = _selectedThemeBackdrop,
            UiFont = _selectedUiFont,
                AuthMode = selected.AuthMode,
                HeaderName = selected.HeaderName,
                TokenBlob = selected.TokenBlob,
                BalancePath = selected.BalancePath,
                Currency = selected.Currency,
                RefreshSeconds = Math.Max(30, selected.RefreshSeconds),
                AutoRefreshEnabled = selected.AutoRefreshEnabled,
                LowThreshold = selected.LowThreshold,
                PetStyle = selectedPetStyle,
                InteractionMode = SelectedTag(InteractionBox, "free"),
                UpdateCheckMode = SelectionTag(UpdateCheckBox, "daily"),
                LastUpdateCheckUtc = _settings.LastUpdateCheckUtc,
                ExtensionUpdateCheckMode = SelectionTag(ExtensionUpdateCheckBox, "daily"),
                LastExtensionUpdateCheckUtc = _settings.LastExtensionUpdateCheckUtc,
                // Carried across rather than re-derived. They belong to the changelog
                // window, which this one does not edit: rebuilding the snapshot without
                // them would reset the watermark to zero and make the next launch announce
                // the entire backlog, and would silently switch the notice back on for
                // anyone who had turned it off.
                // Moved here from the changelog window when that window was removed; the
                // watermark stays with the settings, so this only replaces where the switch
                // lives, not what it means.
                NoticesRecordedSeq = _settings.NoticesRecordedSeq,
            // Carried across rather than omitted. Leaving it out reset the watermark to its default
            // on every save, so the next check saw the whole changelog as unannounced and the pet
            // announced new changelog entries when the only thing that had happened was Apply.
            NoticesSeenSeq = _settings.NoticesSeenSeq,
                NoticesNotify = NoticeBubbleBox.IsChecked == true,
                Scale = ScaleSlider.Value,
                Volume = VolumeSlider.Value,
                Sound = SoundBox.IsChecked == true,
                Bubble = BubbleBox.IsChecked == true,
                InteractionEffects = InteractionEffectsBox.IsChecked == true,
                NavigationAnimations = NavigationAnimationsBox.IsChecked == true,
                NavigationCollapsed = _navigationCollapsed,
                RandomEasterEggs = EasterEggsBox.IsChecked == true,
                CodexTaskIntegration = FollowCodexBox.IsChecked == true,
                DeepSeekHarnessIntegration = FollowDeepSeekHarnessBox.IsChecked == true,
                GeminiTaskIntegration = FollowGeminiBox.IsChecked == true,
                QwenTaskIntegration = FollowQwenBox.IsChecked == true,
                ClaudeTaskIntegration = FollowClaudeBox.IsChecked == true,
                OtherTaskIntegration = FollowOtherBox.IsChecked == true,
                StartWithWindows = startupEnabled,
                WindowX = _settings.WindowX,
                WindowY = _settings.WindowY,
                Flipped = _settings.Flipped,
                Monitors = _profiles.Select(CloneProfile).ToList(),
                SelectedMonitorId = selected.Id
            };
            // Bring every client's hook in line with its switch. A client that is
            // not installed is skipped rather than reported, so a switch can be
            // turned on before its client exists. The work runs off the UI thread
            // because each install starts a PowerShell process.
            var syncTargets = new List<(TaskClient Client, bool Enabled)>
            {
                (TaskClient.Codex, updated.CodexTaskIntegration),
                (TaskClient.DeepSeekHarness, updated.DeepSeekHarnessIntegration),
                (TaskClient.Gemini, updated.GeminiTaskIntegration),
                (TaskClient.Qwen, updated.QwenTaskIntegration),
                (TaskClient.Claude, updated.ClaudeTaskIntegration)
            };
            var installedBefore = syncTargets.Select(target => ClientHookInstaller.IsInstalled(target.Client)).ToArray();
            var syncResult = await Task.Run(() =>
            {
                var ok = ClientHookInstaller.TrySync(syncTargets, removeDisabled: true, out var message);
                return (Ok: ok, Error: message);
            });
            if (!syncResult.Ok)
            {
                MessageText.Text = $"AI 联动配置失败：{syncResult.Error}";
                return;
            }
            var hookChanged = syncTargets
                .Select((target, index) => installedBefore[index] != ClientHookInstaller.IsInstalled(target.Client))
                .Any(changed => changed);
            _store.Save(updated);
            _hasUnsavedChanges = false;
            _settings.Language = updated.Language;
            _settings.ThemeId = updated.ThemeId;
            _settings.ThemeMode = updated.ThemeMode;
            _settings.ThemeBackdrop = updated.ThemeBackdrop;
            _settings.NavigationAnimations = updated.NavigationAnimations;
            _settings.NavigationCollapsed = updated.NavigationCollapsed;
            _settings.UpdateCheckMode = updated.UpdateCheckMode;
            _settings.ExtensionUpdateCheckMode = updated.ExtensionUpdateCheckMode;
            _settings.LastUpdateCheckUtc = updated.LastUpdateCheckUtc;
            _settings.LastExtensionUpdateCheckUtc = updated.LastExtensionUpdateCheckUtc;
            AppLocalization.Apply(this, updated.Language);
            RefreshLanguageSelector(updated.Language, selectLanguage: false);
            UpdatePresetUi(false);
            UpdatePetStyleAvailability();
            UpdateClientAvailability();
            UpdateExtensionButtons();
            if (!StartupManager.SetEnabled(startupEnabled))
            {
                MessageText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
                MessageText.Text = "设置已保存，但开机启动项写入失败，请检查 Windows 权限。";
                return;
            }
            if (!testConnection)
            {
                MessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
                var language = SelectedTag(LanguageBox, _settings.Language);
                MessageText.Text = AppLocalization.Text(language, "设置已生效。", "Settings applied.");
                if (closeAfterSave) CompleteAndClose();
                else NotifySettingsApplied();
                return;
            }
            if (hookChanged && (updated.CodexTaskIntegration || updated.DeepSeekHarnessIntegration || updated.GeminiTaskIntegration || updated.QwenTaskIntegration || updated.ClaudeTaskIntegration))
            {
                System.Windows.MessageBox.Show(this, "AI 联动已更新。Codex 会在出现 Hook 审核提示时请求信任；尚未安装的客户端会在安装后自动写入配置。之后任务开始、完成或停止都会自动通知桌宠。", "BalancePet", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            if (string.IsNullOrWhiteSpace(selectedToken))
            {
                MessageText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
                MessageText.Text = "设置已保存；当前账户未填写访问令牌，已跳过余额 API 测试。";
                CompleteAndClose();
                return;
            }

            try
            {
                using var client = Networking.CreateClient(TimeSpan.FromSeconds(15));
                var snapshot = await new JsonBalanceProvider(client).FetchWithRetryAsync(selected, selectedToken);
                MessageText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
                var resolved = selected.PresetId == BalancePresetCatalog.Auto && !string.IsNullOrWhiteSpace(snapshot.ResolvedPresetId)
                    ? $"；已识别为 {BalancePresetCatalog.DisplayName(snapshot.ResolvedPresetId, updated.Language)}"
                    : "";
                MessageText.Text = $"连接成功：{snapshot.Amount:0.00} {snapshot.Currency}{resolved}";
                CompleteAndClose();
            }
            catch (Exception error)
            {
                MessageText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
                var message = $"设置已保存，但余额 API 测试失败：{error.Message}";
                MessageText.Text = message;
                System.Windows.MessageBox.Show(this, message, "BalancePet", MessageBoxButton.OK, MessageBoxImage.Warning);
                CompleteAndClose();
            }
        }
        catch (Exception error)
        {
            MessageText.Text = error.Message;
        }
    }

    private void OnAuthModeChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncComboDisplay(AuthModeBox);
        if (!_suppressChangeTracking) MarkSettingsDirty();
        if (HeaderBox is null || AuthModeBox is null) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var custom = SelectedTag(AuthModeBox, "bearer") == "custom";
        var presetUsesSiteUrl = BalancePresetCatalog.UsesSiteUrl(SelectedTag(PresetBox, BalancePresetCatalog.Custom));
        HeaderBox.IsEnabled = custom && !presetUsesSiteUrl;
        if (!custom) HeaderBox.Text = "Authorization";
        AuthHint.Text = SelectedTag(AuthModeBox, "bearer") switch
        {
            "bearer" => AppLocalization.Text(language, "令牌框只填写令牌本身，程序会自动发送 Authorization: Bearer <令牌>。", "Enter only the token; BalancePet sends Authorization: Bearer <token> automatically."),
            "authorization" => AppLocalization.Text(language, "令牌框填写完整 Authorization 值，例如 Bearer sk-...。", "Enter the complete Authorization value, for example Bearer sk-...."),
            "websee-session" => AppLocalization.Text(language, "令牌框填写 websee-session 会话值；仅适用于提供该接口格式的中转站。", "Enter the websee-session value; this works only with relays that provide this interface format."),
            "x-api-key" => AppLocalization.Text(language, "令牌框填写 API key，程序会发送 x-api-key 请求头。", "Enter the API key; BalancePet sends it in the x-api-key header."),
            "custom" => AppLocalization.Text(language, "令牌框填写 Header 值；上方 Header 名必须与中转站文档完全一致。", "Enter the header value; the header name above must exactly match the relay documentation."),
            _ => AppLocalization.Text(language, "请以中转站接口文档要求为准。", "Follow the requirements in the relay API documentation.")
        };
    }

    private async void OnReadBrowserSessionClick(object sender, RoutedEventArgs e)
    {
        var site = BalancePresetCatalog.UsesSiteUrl(SelectedTag(PresetBox, BalancePresetCatalog.Custom))
            ? SiteUrlBox.Text
            : EndpointBox.Text;
        site = BalancePresetCatalog.NormalizeSiteUrl(site);
        if (!Uri.TryCreate(site, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            BrowserSessionStatusText.Text = "请先填写有效的中转站地址。";
            return;
        }

        var browser = SelectedTag(BrowserSessionBrowserBox, "edge");
        ReadBrowserSessionButton.IsEnabled = false;
        BrowserSessionStatusText.Text = $"正在读取 {uri.Host} 的本地浏览器会话……";
        try
        {
            var result = await new BrowserSessionReader().ReadAsync(site, browser);
            if (result.Success)
            {
                WebSessionBox.Password = result.CookieHeader;
                BrowserSessionStatusText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
                BrowserSessionStatusText.Text = $"{result.Message}（{result.CookieCount} 项，点击“应用”或“确定”后加密保存）";
                MarkSettingsDirty();
            }
            else
            {
                BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
                BrowserSessionStatusText.Text = result.Message;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
            BrowserSessionStatusText.Text = $"读取浏览器会话失败：{error.Message}";
        }
        finally
        {
            ReadBrowserSessionButton.IsEnabled = true;
        }
    }

    private void OnStartBrowserBridgeClick(object sender, RoutedEventArgs e)
    {
        if (_browserSessionBridge is null)
        {
            BrowserSessionStatusText.Text = "当前主程序未启用浏览器桥接服务。";
            return;
        }
        var profile = CurrentProfile;
        var site = BalancePresetCatalog.UsesSiteUrl(SelectedTag(PresetBox, BalancePresetCatalog.Custom))
            ? SiteUrlBox.Text
            : EndpointBox.Text;
        site = BalancePresetCatalog.NormalizeSiteUrl(site);
        if (profile is null || !Uri.TryCreate(site, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            BrowserSessionStatusText.Text = "请先填写有效的中转站地址。";
            return;
        }
        try
        {
            var pairing = _browserSessionBridge.StartPairing(profile.Id, site);
            BrowserPairingCodeText.Text = pairing.Code;
            BrowserSessionStatusText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            BrowserSessionStatusText.Text = $"请在浏览器扩展中输入配对码，当前站点：{pairing.SiteHost}。配对码初始 10 分钟有效；每次同步成功会自动续期。";
        }
        catch (SocketException)
        {
            BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
            BrowserSessionStatusText.Text = "本机桥接端口被占用，请关闭其他 BalancePet 实例后重试。";
        }
        catch (Exception error)
        {
            BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
            BrowserSessionStatusText.Text = $"启动浏览器桥接失败：{error.Message}";
        }
    }

    private void OnBrowserSessionReceived(object? sender, BrowserSessionReceivedEventArgs args)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!string.Equals(args.ProfileId, _currentProfileId, StringComparison.OrdinalIgnoreCase)) return;
            var browserCredential = args.AuthorizationHeader;
            if (!string.IsNullOrWhiteSpace(browserCredential) && !string.IsNullOrWhiteSpace(args.UserId))
                browserCredential += $"; New-Api-User: {args.UserId}";
            WebSessionBox.Password = !string.IsNullOrWhiteSpace(browserCredential)
                ? browserCredential
                : args.CookieHeader;
            BrowserPairingCodeText.Text = "已连接";
            BrowserSessionStatusText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            var authHint = string.IsNullOrWhiteSpace(args.AuthorizationHeader) ? "" : "、页面访问令牌";
            BrowserSessionStatusText.Text = $"已通过浏览器扩展读取 {args.CookieCount} 项 Cookie、{args.StorageCount} 项网页会话{authHint}；正在解析 {args.UsageResponses.Count} 项用量响应。点击“应用”或“确定”后加密保存。";
            MarkSettingsDirty();
            _ = ImportBrowserUsageResponsesAsync(args.UsageResponses);
        }));
    }

    private async Task ImportBrowserUsageResponsesAsync(IReadOnlyList<BrowserUsageResponse> responses)
    {
        if (responses.Count == 0) return;
        try
        {
            var profile = CurrentProfile;
            if (profile is null) return;
            using var client = Networking.CreateClient(TimeSpan.FromSeconds(20));
            var count = await new NewApiUsageProvider(client).ImportRawResponsesAsync(profile, responses);
            await Dispatcher.InvokeAsync(() =>
            {
                if (count > 0)
                    BrowserSessionStatusText.Text = $"已通过浏览器扩展读取会话，并解析到 {count} 条费用明细；点击“应用”或“确定”保存会话。";
                else
                    BrowserSessionStatusText.Text = $"会话已读取 {responses.Count} 项页面响应，但仍未解析到费用字段。";
            });
        }
        catch (Exception error)
        {
            await Dispatcher.InvokeAsync(() => BrowserSessionStatusText.Text = $"会话已读取，但用量明细解析失败：{error.Message}");
        }
    }

    private async void OnTestUsageSessionClick(object sender, RoutedEventArgs e)
    {
        var profile = CurrentProfile;
        if (profile is null)
        {
            BrowserSessionStatusText.Text = "请先选择一个监控账户。";
            return;
        }

        SaveCurrentProfileFields();
        string token;
        string session;
        try
        {
            token = _tokens.Unprotect(profile.TokenBlob);
            session = !string.IsNullOrWhiteSpace(WebSessionBox.Password)
                ? WebSessionBox.Password
                : _tokens.Unprotect(profile.WebSessionBlob);
        }
        catch (Exception error)
        {
            BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
            BrowserSessionStatusText.Text = $"会话解密失败：{error.Message}";
            return;
        }

        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(session))
        {
            BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
            BrowserSessionStatusText.Text = "没有可测试的访问令牌或网页会话，请先完成配对并点击应用。";
            return;
        }

        TestUsageSessionButton.IsEnabled = false;
        BrowserSessionStatusText.Foreground = ThemeBrush("MutedBrush", System.Windows.Media.Brushes.Gray);
        BrowserSessionStatusText.Text = "正在测试余额接口和逐条费用明细……";
        try
        {
            using var client = Networking.CreateClient(TimeSpan.FromSeconds(20));
            var balanceText = "余额接口未测试";
            if (!string.IsNullOrWhiteSpace(token))
            {
                var snapshot = await new JsonBalanceProvider(client).FetchWithRetryAsync(profile, token);
                balanceText = $"余额 {snapshot.Amount:0.00} {snapshot.Currency}";
            }

            var records = NewApiUsageProvider.Supports(profile)
                ? await new NewApiUsageProvider(client).FetchRecentAsync(profile, token, session)
                : Array.Empty<NewApiUsageRecord>();
            BrowserSessionStatusText.Foreground = ThemeBrush("AccentBrush", System.Windows.Media.Brushes.SeaGreen);
            BrowserSessionStatusText.Text = records.Count > 0
                ? $"测试成功：{balanceText}；读取到 {records.Count} 条费用明细。"
                : $"{balanceText}；会话已保存，但费用明细接口暂未返回可解析记录。";
        }
        catch (Exception error)
        {
            BrowserSessionStatusText.Foreground = ThemeBrush("WarningTextBrush", System.Windows.Media.Brushes.DarkOrange);
            BrowserSessionStatusText.Text = $"会话已保存，但接口测试失败：{error.Message}";
        }
        finally
        {
            TestUsageSessionButton.IsEnabled = true;
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;
        MarkSettingsDirty();
        SyncComboDisplay(LanguageBox);
        var language = SelectedTag(LanguageBox, "zh-CN");
        AppLocalization.Apply(this, language);
        RefreshLanguageSelector(language, selectLanguage: false);
        RefreshThemeList(SelectedTag(ThemeBox, _settings.ThemeId));
        ApplySelectedTheme();
        UpdateThemeSummary();
        SyncAllComboDisplays();
        UpdatePetStyleAvailability();
        UpdatePresetUi(false);
        RefreshExtensionList();
        RefreshExtensionActionLabels(language);
        RefreshPluginCatalogLabels(language);
        RefreshVersionPanelLabels(language);
        RebuildPluginCatalogItems();
        ApplyNavigationState();
    }
    private void CompleteAndClose()
    {
        _allowCloseWithoutPrompt = true;
        NotifySettingsApplied();
        Close();
    }

    private System.Windows.Media.Brush ThemeBrush(string resourceKey, System.Windows.Media.Brush fallback)
        => TryFindResource(resourceKey) as System.Windows.Media.Brush ?? fallback;

    private void NotifySettingsApplied()
    {
        SettingsApplied = true;
        SettingsAppliedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _allowCloseWithoutPrompt = true;
        Close();
    }
}
