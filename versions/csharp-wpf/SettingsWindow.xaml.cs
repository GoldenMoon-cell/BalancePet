using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly PetExtensionManager _extensions = new();
    private readonly FeatureExtensionManager _featureExtensions;
    private readonly ExtensionPackageCatalog _extensionLibrary = new();
    private readonly HttpClient _extensionUpdateHttpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
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

    public SettingsWindow(SettingsStore store, DpapiTokenStore tokens, PetSettings settings, FeatureExtensionManager? featureExtensions = null)
    {
        InitializeComponent(); _store = store; _tokens = tokens; _settings = settings; _featureExtensions = featureExtensions ?? new FeatureExtensionManager();
        _extensionUpdates = new ExtensionUpdateService(_extensionUpdateHttpClient);
        _pluginCatalog = new PluginCatalogService(_extensionUpdateHttpClient);
        Closed += (_, _) => { _pluginCatalogCancellation.Cancel(); _pluginCatalogCancellation.Dispose(); _extensionUpdateHttpClient.Dispose(); };
        AddInstalledPetStyles();
        RefreshExtensionList();
        UpdatePetStyleAvailability();
        _profiles = settings.Monitors is { Count: > 0 }
            ? settings.Monitors.Select(CloneProfile).ToList()
            : new List<MonitorProfile> { CreateProfileFromLegacy(settings) };
        RefreshProfileList(settings.SelectedMonitorId);
        SelectByTag(PetStyleBox, settings.PetStyle); SelectByTag(InteractionBox, settings.InteractionMode); SelectByTag(UpdateCheckBox, settings.UpdateCheckMode); SelectByTag(ExtensionUpdateCheckBox, settings.ExtensionUpdateCheckMode);
        _suppressLanguageChange = true;
        SelectByTag(LanguageBox, settings.Language);
        _suppressLanguageChange = false;
        ScaleSlider.Value = Math.Clamp(settings.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(settings.Volume, 0, 1); SoundBox.IsChecked = settings.Sound; BubbleBox.IsChecked = settings.Bubble; InteractionEffectsBox.IsChecked = settings.InteractionEffects; EasterEggsBox.IsChecked = settings.RandomEasterEggs; FollowCodexBox.IsChecked = settings.CodexTaskIntegration; AccountStatusBox.IsChecked = settings.CCSwitchIntegration; NotificationsBox.IsChecked = settings.SystemNotifications; StartupBox.IsChecked = settings.StartWithWindows || StartupManager.IsEnabled();
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        AppLocalization.Apply(this, settings.Language);
        RefreshLanguageSelector(settings.Language, selectLanguage: false);
        SyncAllComboDisplays();
        UpdatePetStyleAvailability();
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

    private void AddInstalledPetStyles()
    {
        if (PetStyleBox is null) return;
        var builtInIds = PetStyleCatalog.All.Select(definition => definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableIds = PetStyleCatalog.GetAvailableExtensionStyles().Select(definition => definition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in PetStyleBox.Items.OfType<ComboBoxItem>().ToArray())
        {
            if (item.Tag is string id && !builtInIds.Contains(id) && !availableIds.Contains(id))
                PetStyleBox.Items.Remove(item);
        }
        var existing = PetStyleBox.Items.OfType<ComboBoxItem>()
            .Select(item => item.Tag?.ToString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PetStyleCatalog.GetAvailableExtensionStyles())
        {
            if (!existing.Add(definition.Id)) continue;
            PetStyleBox.Items.Add(new ComboBoxItem
            {
                Tag = definition.Id,
                Content = definition.ChineseName,
                ToolTip = $"{AppLocalization.Text(_settings.Language, "资源扩展：", "Resource extension: ")}{definition.Id}"
            });
        }
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

    private static void SyncComboDisplay(System.Windows.Controls.ComboBox? box)
    {
        if (box?.SelectedItem is not ComboBoxItem item) return;
        var text = item.Content?.ToString() ?? string.Empty;
        if (!string.Equals(box.Text, text, StringComparison.Ordinal)) box.Text = text;
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
    }

    private void OnComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox box) SyncComboDisplay(box);
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
    }

    private void RefreshExtensionList()
    {
        if (ExtensionListBox is null) return;
        var pets = _extensions.GetInstalled();
        var features = _featureExtensions.GetInstalled();
        var entries = _extensionLibrary.BuildEntries(pets, features).ToList();
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
        ExtensionListBox.ItemsSource = entries.OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToArray();
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
                packagePath = await _extensionUpdates.DownloadAsync(selected.RemoteUpdate);
                try { _extensionLibrary.ImportPackage(packagePath); } finally { try { File.Delete(packagePath); } catch (IOException) { } }
                packagePath = _extensionLibrary.Scan().First(value => string.Equals(value.Id, selected.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Version, selected.RemoteUpdate.Version, StringComparison.OrdinalIgnoreCase)).PackagePath;
            }
            if (string.IsNullOrWhiteSpace(packagePath)) return;
            if (selected.Type == "feature") _featureExtensions.InstallFeaturePackage(packagePath); else _extensions.InstallPetPackage(packagePath);
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
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        AppLocalization.Apply(this, language);
        RefreshLanguageSelector(language, selectLanguage: false);
        SyncAllComboDisplays();
        UpdatePresetUi(false);
        UpdatePetStyleAvailability();
        UpdateExtensionButtons();
        RefreshExtensionActionLabels(language);
        RefreshPluginCatalogLabels(language);
        await LoadPluginCatalogAsync(manual: false);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || sender is not System.Windows.Controls.TabControl tabs || tabs.SelectedItem is not TabItem selectedTab) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        AppLocalization.Apply(selectedTab, language);
        SyncAllComboDisplays();
    }

    private void OnScanExtensionLibrary(object sender, RoutedEventArgs e)
    {
        try
        {
            _extensionLibrary.EnsureDirectory();
            RefreshExtensionList();
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
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
        ExtensionCheckButton.ToolTip = AppLocalization.Text(language, "检查扩展更新", "Check extension updates");
    }

    private readonly HashSet<string> _pluginCatalogBusyIds = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshPluginCatalogLabels(string language)
    {
        if (PluginCatalogTitleText is null) return;
        ExtensionManagementTitleText.Text = AppLocalization.Text(language, "扩展管理", "Extension management");
        ExtensionManagementHintText.Text = AppLocalization.Text(language, "在线插件库负责发现扩展，本地扩展区负责安装、启用、更新和启动。", "Discover extensions online; manage installation, enablement, updates, and launching locally.");
        PluginCatalogTitleText.Text = AppLocalization.Text(language, "在线插件库", "Online plugin catalog");
        PluginCatalogHintText.Text = AppLocalization.Text(language, "从 BalancePet 官方目录发现插件；下载后仍会执行本地安全校验。", "Discover plugins from the curated BalancePet catalog; every download is still verified locally.");
        PluginCatalogRefreshButton.Content = AppLocalization.Text(language, "刷新目录", "Refresh catalog");
        PluginCatalogRefreshButton.ToolTip = AppLocalization.Text(language, "刷新在线插件目录", "Refresh the online plugin catalog");
        PluginCatalogSearchBox.ToolTip = AppLocalization.Text(language, "搜索插件名称、作者或分类", "Search by plugin name, author, or category");
        LocalExtensionsTitleText.Text = AppLocalization.Text(language, "本地扩展", "Local extensions");
        LocalExtensionsHintText.Text = AppLocalization.Text(language, "已下载到扩展库的 ZIP 会显示在这里；扫描到不代表已经安装。", "ZIPs downloaded to the local library appear here; scanning does not install them.");
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
            RebuildPluginCatalogItems();
            if (result.FromRemote)
            {
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"已从官方目录加载 {_pluginCatalogEntries.Count} 个插件。", $"Loaded {_pluginCatalogEntries.Count} plugin(s) from the curated catalog.");
            }
            else if (_pluginCatalogEntries.Count > 0)
            {
                var suffix = string.IsNullOrWhiteSpace(result.Error) ? "" : $"（在线目录暂时不可用：{result.Error}）";
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"网络不可用，已使用本地缓存目录，共 {_pluginCatalogEntries.Count} 个插件{suffix}", $"Online catalog unavailable; showing {_pluginCatalogEntries.Count} cached plugin(s).{(string.IsNullOrWhiteSpace(result.Error) ? "" : $" {result.Error}")}");
            }
            else
            {
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"插件目录加载失败：{result.Error ?? "暂无可用条目"}", $"Could not load the plugin catalog: {result.Error ?? "No entries are available."}");
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
        {
            if (!_pluginCatalogCancellation.IsCancellationRequested)
            {
                PluginCatalogStatusText.Text = AppLocalization.Text(language, $"插件目录加载失败：{error.Message}", $"Could not load the plugin catalog: {error.Message}");
                if (manual) ShowExtensionError($"插件目录加载失败：{error.Message}", $"Could not load the plugin catalog: {error.Message}");
            }
        }
        finally
        {
            if (!_pluginCatalogCancellation.IsCancellationRequested) PluginCatalogRefreshButton.IsEnabled = true;
        }
    }

    private void RebuildPluginCatalogItems()
    {
        if (PluginCatalogListBox is null) return;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var english = AppLocalization.IsEnglish(language);
        var installed = _extensionLibrary.BuildEntries(_extensions.GetInstalled(), _featureExtensions.GetInstalled())
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var query = PluginCatalogSearchBox?.Text?.Trim() ?? "";
        var views = _pluginCatalogEntries
            .Where(record => string.IsNullOrWhiteSpace(query) || string.Join(" ", record.Id, record.Name, record.NameEn, record.Author, record.Description, record.DescriptionEn, string.Join(" ", record.Categories)).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .Select(record =>
            {
                installed.TryGetValue(record.Id, out var local);
                var view = new PluginCatalogItemView(record, local, english) { IsBusy = _pluginCatalogBusyIds.Contains(record.Id) };
                return view;
            })
            .ToArray();
        PluginCatalogListBox.ItemsSource = views;
        PluginCatalogCountText.Text = AppLocalization.Text(language, $"{views.Length} 个插件", $"{views.Length} plugin(s)");
    }

    private async void OnInstallPluginCatalogItem(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.DataContext is not PluginCatalogItemView item || !item.CanInstall || !_pluginCatalogBusyIds.Add(item.Id)) return;
        RebuildPluginCatalogItems();
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        try
        {
            var record = item.Record;
            var downloaded = await _extensionUpdates.DownloadAsync(new ExtensionUpdateRelease
            {
                Id = record.Id,
                Type = record.Type,
                Version = record.Version,
                PackageName = Path.GetFileName(new Uri(record.DownloadUrl).AbsolutePath),
                DownloadUrl = record.DownloadUrl,
                Digest = $"sha256:{record.Sha256}"
            }, _pluginCatalogCancellation.Token);
            try
            {
                _extensionLibrary.ImportPackage(downloaded);
                var package = _extensionLibrary.Scan().FirstOrDefault(value => string.Equals(value.Id, record.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Version, record.Version, StringComparison.OrdinalIgnoreCase));
                if (package is null) throw new InvalidDataException("下载的插件清单与目录版本不一致。");
                if (record.Type.Equals("feature", StringComparison.OrdinalIgnoreCase))
                    _featureExtensions.InstallFeaturePackage(package.PackagePath);
                else if (record.Type.Equals("pet", StringComparison.OrdinalIgnoreCase))
                {
                    _extensions.InstallPetPackage(package.PackagePath);
                    AddInstalledPetStyles();
                    UpdatePetStyleAvailability();
                }
                else throw new InvalidDataException("无法识别插件类型。");
            }
            finally
            {
                try { File.Delete(downloaded); } catch (IOException) { }
            }
            RefreshExtensionList();
            RebuildPluginCatalogItems();
            ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            var name = AppLocalization.IsEnglish(language) && !string.IsNullOrWhiteSpace(record.NameEn) ? record.NameEn : record.Name;
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
        try
        {
            var path = _extensionLibrary.ImportPackage(dialog.FileName);
            RefreshExtensionList();
            ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
            ExtensionMessageText.Text = AppLocalization.Text(language, $"已放入扩展库：{Path.GetFileName(path)}。可使用对应行右侧的安装图标。", $"Added to extension library: {Path.GetFileName(path)}. Use the install icon on its row.");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FileNotFoundException or NotSupportedException or JsonException)
        {
            ShowExtensionError($"导入失败：{error.Message}", $"Import failed: {error.Message}");
        }
    }

    private void OnInstallSelectedExtension(object sender, RoutedEventArgs e)
    {
        if (GetExtensionEntry(sender) is not ExtensionCatalogEntry selected || selected.Package is null) return;
        try
        {
            if (selected.Package.Type.Equals("feature", StringComparison.OrdinalIgnoreCase))
                _featureExtensions.InstallFeaturePackage(selected.Package.PackagePath);
            else if (selected.Package.Type.Equals("pet", StringComparison.OrdinalIgnoreCase))
            {
                _extensions.InstallPetPackage(selected.Package.PackagePath);
                AddInstalledPetStyles();
                UpdatePetStyleAvailability();
            }
            else throw new InvalidDataException("无法识别扩展类型。");
            RefreshExtensionList();
            ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
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
        var enabled = !selected.IsEnabled;
        var changed = selected.Pet is not null
            ? _extensions.SetEnabled(selected.Pet.Manifest.Id, enabled)
            : selected.Feature is not null && _featureExtensions.SetEnabled(selected.Feature.Manifest.Id, enabled);
        if (!changed) return;
        if (selected.Pet is PetExtensionInfo petSelected && !enabled && string.Equals(SelectedTag(PetStyleBox, "deepseek"), petSelected.StyleId, StringComparison.OrdinalIgnoreCase))
            SelectByTag(PetStyleBox, "deepseek");
        if (selected.Pet is not null)
        {
            AddInstalledPetStyles();
            UpdatePetStyleAvailability();
        }
        RefreshExtensionList();
        ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
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
            ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            ExtensionMessageText.Text = AppLocalization.Text(language, "功能扩展已启动。", "Feature extension launched.");
        }
        else
        {
            ExtensionMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
            ExtensionMessageText.Text = AppLocalization.Text(language, $"启动失败：{error}", $"Launch failed: {error}");
        }
        RefreshExtensionList();
    }

    private void OnUninstallExtension(object sender, RoutedEventArgs e)
    {
        if (GetExtensionEntry(sender) is not ExtensionCatalogEntry selected || !selected.IsInstalled) return;
        var selectedName = selected.Pet?.Manifest.Name ?? selected.Feature?.Manifest.Name ?? selected.Id;
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var answer = System.Windows.MessageBox.Show(this,
            AppLocalization.Text(language, $"确定卸载扩展“{selectedName}”吗？这只会删除它的扩展目录，不会影响主程序和其他扩展。", $"Uninstall extension \"{selectedName}\"? Only its extension directory will be removed; the main program and other extensions are unaffected."),
            AppLocalization.Text(language, "卸载扩展", "Uninstall extension"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        var removed = selected.Pet is not null
            ? _extensions.Uninstall(selected.Pet.Manifest.Id)
            : selected.Feature is not null && _featureExtensions.Uninstall(selected.Feature.Manifest.Id);
        if (!removed) return;
        if (selected.Pet is PetExtensionInfo petSelected && string.Equals(SelectedTag(PetStyleBox, "deepseek"), petSelected.StyleId, StringComparison.OrdinalIgnoreCase))
            SelectByTag(PetStyleBox, "deepseek");
        if (selected.Pet is not null)
        {
            AddInstalledPetStyles();
            UpdatePetStyleAvailability();
        }
        RefreshExtensionList();
        ExtensionMessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
        ExtensionMessageText.Text = AppLocalization.Text(language, "扩展已卸载；扩展库中的 ZIP 仍然保留。点击“扫描扩展库”可再次安装。", "Extension uninstalled; the ZIP in the library was kept. Scan the library to install it again.");
    }

    private ExtensionCatalogEntry? GetExtensionEntry(object sender)
        => (sender as FrameworkElement)?.DataContext as ExtensionCatalogEntry ?? ExtensionListBox.SelectedItem as ExtensionCatalogEntry;

    private void ShowExtensionError(string chinese, string english)
    {
        ExtensionMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
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
        AuthMode = profile.AuthMode,
        HeaderName = profile.HeaderName,
        TokenBlob = profile.TokenBlob,
        BalancePath = profile.BalancePath,
        Currency = profile.Currency,
        RefreshSeconds = profile.RefreshSeconds,
        AutoRefreshEnabled = profile.AutoRefreshEnabled,
        LowThreshold = profile.LowThreshold,
        Enabled = profile.Enabled
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
        TokenBox.Clear();
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
        UpdatePresetUi(true);
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
        profile.Currency = string.IsNullOrWhiteSpace(CurrencyBox.Text) ? "USD" : CurrencyBox.Text.Trim().ToUpperInvariant();
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
        if (!string.IsNullOrWhiteSpace(TokenBox.Password)) profile.TokenBlob = _tokens.Protect(TokenBox.Password);
    }

    private void OnProfileNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressProfileChange || ProfileBox is null || ProfileBox.SelectedItem is not ComboBoxItem item) return;
        item.Content = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "监控账户" : ProfileNameBox.Text.Trim();
    }

    private void OnRefreshModeChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncComboDisplay(RefreshBox);
        if (_suppressRefreshChange) return;
        UpdateRefreshModeVisibility();
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncComboDisplay(PresetBox);
        if (_suppressPresetChange || PresetBox is null || SiteUrlBox is null) return;
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
        UpdatePresetPreview();
    }

    private void UpdatePresetUi(bool updatePreview)
    {
        if (PresetBox is null || SiteUrlBox is null || EndpointBox is null || AuthModeBox is null || PathBox is null) return;
        var presetId = SelectedTag(PresetBox, BalancePresetCatalog.Custom);
        var usesSiteUrl = BalancePresetCatalog.UsesSiteUrl(presetId);
        var language = LanguageBox is null ? _settings.Language : SelectedTag(LanguageBox, _settings.Language);
        var visibility = usesSiteUrl ? Visibility.Visible : Visibility.Collapsed;
        SiteUrlLabel.Visibility = visibility;
        SiteUrlBox.Visibility = visibility;
        SiteUrlHint.Visibility = visibility;
        EndpointBox.IsReadOnly = usesSiteUrl;
        AuthModeBox.IsEnabled = !usesSiteUrl;
        PathBox.IsReadOnly = usesSiteUrl;
        EndpointHint.Text = AppLocalization.Text(language,
            usesSiteUrl ? "接口地址由预设自动生成；切换到“自定义接口”后可以手动修改。" : "填写中转站文档中的余额查询 URL，不是网站首页或聊天接口。",
            usesSiteUrl ? "The endpoint is generated by the preset. Switch to Custom endpoint to edit it." : "Enter the balance URL from your relay provider's documentation, not the website or chat endpoint.");
        PresetHint.Text = presetId switch
        {
            BalancePresetCatalog.Auto => AppLocalization.Text(language, "依次尝试同一站点的 /v1/usage 和 /api/usage/token，只执行只读查询。", "Tries /v1/usage and /api/usage/token on the same site using read-only requests."),
            BalancePresetCatalog.V1Usage => AppLocalization.Text(language, "适用于提供 /v1/usage 的中转站；自动识别 balance、remaining 和单位。", "For relays exposing /v1/usage; balance, remaining, and units are detected automatically."),
            BalancePresetCatalog.NewApiToken => AppLocalization.Text(language, "适用于标准 New API 令牌额度接口，并按站点公开的额度与货币设置换算。", "Uses the standard New API token-usage endpoint and the site's published quota and currency settings."),
            _ => AppLocalization.Text(language, "手动填写完整接口、认证方式和 JSON 路径。", "Enter the full endpoint, authentication method, and JSON path manually.")
        };
        if (updatePreview && usesSiteUrl) UpdatePresetPreview();
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
    }

    private void UpdatePresetPreview()
    {
        var presetId = SelectedTag(PresetBox, BalancePresetCatalog.Custom);
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
        RefreshCustomBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
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
        LoadProfile(item.Tag?.ToString() ?? "");
    }

    private void OnAddProfile(object sender, RoutedEventArgs e)
    {
        SaveCurrentProfileFields();
        var profile = new MonitorProfile { Id = Guid.NewGuid().ToString("N"), Name = $"监控账户 {_profiles.Count + 1}", PresetId = BalancePresetCatalog.Auto, Endpoint = "", Enabled = true };
        _profiles.Add(profile);
        RefreshProfileList(profile.Id);
        SiteUrlBox.Focus();
    }

    private void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1) { MessageText.Text = "至少保留一个监控账户。"; return; }
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
            _profiles.Clear();
            if (imported.Monitors is { Count: > 0 }) _profiles.AddRange(imported.Monitors.Select(CloneProfile));
            else _profiles.Add(CreateProfileFromLegacy(imported));
            RefreshProfileList(imported.SelectedMonitorId);
            SelectByTag(PetStyleBox, imported.PetStyle); SelectByTag(InteractionBox, imported.InteractionMode); SelectByTag(UpdateCheckBox, imported.UpdateCheckMode); SelectByTag(ExtensionUpdateCheckBox, imported.ExtensionUpdateCheckMode);
            SelectByTag(LanguageBox, imported.Language);
            ScaleSlider.Value = Math.Clamp(imported.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(imported.Volume, 0, 1);
            SoundBox.IsChecked = imported.Sound; BubbleBox.IsChecked = imported.Bubble; InteractionEffectsBox.IsChecked = imported.InteractionEffects; EasterEggsBox.IsChecked = imported.RandomEasterEggs; AccountStatusBox.IsChecked = imported.CCSwitchIntegration; NotificationsBox.IsChecked = imported.SystemNotifications; StartupBox.IsChecked = imported.StartWithWindows;
            OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
            AppLocalization.Apply(this, imported.Language);
            RefreshLanguageSelector(imported.Language, selectLanguage: false);
            TokenBox.Clear();
            MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            MessageText.Text = "设置已导入。令牌不会从文件导入，请在各监控账户中重新填写令牌。";
        }
        catch (Exception error)
        {
            MessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
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
                auth_mode = selected.AuthMode,
                header_name = selected.HeaderName,
                balance_path = selected.BalancePath,
                currency = selected.Currency,
                refresh_seconds = selected.RefreshSeconds,
                auto_refresh_enabled = selected.AutoRefreshEnabled,
                low_threshold = selected.LowThreshold,
                pet_style = SelectedTag(PetStyleBox, "deepseek"),
                interaction_mode = SelectedTag(InteractionBox, "free"),
                update_check_mode = SelectionTag(UpdateCheckBox, "daily"),
                extension_update_check_mode = SelectionTag(ExtensionUpdateCheckBox, "daily"),
                pet_scale = ScaleSlider.Value,
                sound = SoundBox.IsChecked == true,
                volume = VolumeSlider.Value,
                bubble = BubbleBox.IsChecked == true,
                interaction_effects = InteractionEffectsBox.IsChecked == true,
                random_easter_eggs = EasterEggsBox.IsChecked == true,
                codex_task_integration = FollowCodexBox.IsChecked == true,
                account_status_integration = AccountStatusBox.IsChecked == true,
                system_notifications = NotificationsBox.IsChecked == true,
                start_with_windows = StartupBox.IsChecked == true,
                selected_monitor_id = selected.Id,
                monitors = _profiles.Select(profile => new
                {
                    id = profile.Id,
                    name = profile.Name,
                    preset_id = profile.PresetId,
                    site_url = profile.SiteUrl,
                    endpoint = profile.Endpoint,
                    auth_mode = profile.AuthMode,
                    header_name = profile.HeaderName,
                    balance_path = profile.BalancePath,
                    currency = profile.Currency,
                    refresh_seconds = Math.Max(30, profile.RefreshSeconds),
                    auto_refresh_enabled = profile.AutoRefreshEnabled,
                    low_threshold = profile.LowThreshold,
                    enabled = profile.Enabled
                }).ToArray()
            };
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, options));
            MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            MessageText.Text = "设置已导出。文件中不包含访问令牌。";
        }
        catch (Exception error)
        {
            MessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
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
            if (!BalancePresetCatalog.UsesSiteUrl(profile.PresetId) && string.IsNullOrWhiteSpace(profile.BalancePath)) { MessageText.Text = $"监控账户“{profile.Name}”的余额 JSON 路径不能为空。"; RefreshProfileList(profile.Id); return; }
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
            var selectedPetStyle = SelectedTag(PetStyleBox, "deepseek");
            if (!PetStyleCatalog.IsAvailable(selectedPetStyle)) selectedPetStyle = "deepseek";
            var updated = new PetSettings
            {
                Endpoint = selected.Endpoint,
                Language = SelectedTag(LanguageBox, "zh-CN"),
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
                Scale = ScaleSlider.Value,
                Volume = VolumeSlider.Value,
                Sound = SoundBox.IsChecked == true,
                Bubble = BubbleBox.IsChecked == true,
                InteractionEffects = InteractionEffectsBox.IsChecked == true,
                RandomEasterEggs = EasterEggsBox.IsChecked == true,
                CodexTaskIntegration = FollowCodexBox.IsChecked == true,
                CCSwitchIntegration = AccountStatusBox.IsChecked == true,
                SystemNotifications = NotificationsBox.IsChecked == true,
                StartWithWindows = startupEnabled,
                WindowX = _settings.WindowX,
                WindowY = _settings.WindowY,
                Flipped = _settings.Flipped,
                Monitors = _profiles.Select(CloneProfile).ToList(),
                SelectedMonitorId = selected.Id
            };
            var hookChanged = false;
            var hookWasInstalled = CodexHookInstaller.IsInstalled();
            if (updated.CodexTaskIntegration)
            {
                if (!CodexHookInstaller.TryInstall(out var hookError))
                {
                    MessageText.Text = $"Codex Hook 安装失败：{hookError}";
                    return;
                }
                // Reinstalling also refreshes the script after a BalancePet update.
                hookChanged = !hookWasInstalled;
            }
            else if (!updated.CodexTaskIntegration && CodexHookInstaller.IsInstalled())
            {
                if (!CodexHookInstaller.TryUninstall(out var hookError))
                {
                    MessageText.Text = $"Codex Hook 移除失败：{hookError}";
                    return;
                }
                hookChanged = true;
            }
            _store.Save(updated);
            _settings.Language = updated.Language;
            _settings.UpdateCheckMode = updated.UpdateCheckMode;
            _settings.ExtensionUpdateCheckMode = updated.ExtensionUpdateCheckMode;
            _settings.LastUpdateCheckUtc = updated.LastUpdateCheckUtc;
            _settings.LastExtensionUpdateCheckUtc = updated.LastExtensionUpdateCheckUtc;
            AppLocalization.Apply(this, updated.Language);
            RefreshLanguageSelector(updated.Language, selectLanguage: false);
            UpdatePresetUi(false);
            UpdatePetStyleAvailability();
            UpdateExtensionButtons();
            if (!StartupManager.SetEnabled(startupEnabled))
            {
                MessageText.Foreground = System.Windows.Media.Brushes.DarkOrange;
                MessageText.Text = "设置已保存，但开机启动项写入失败，请检查 Windows 权限。";
                return;
            }
            if (!testConnection)
            {
                MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                var language = SelectedTag(LanguageBox, _settings.Language);
                MessageText.Text = AppLocalization.Text(language, "设置已保存。", "Settings saved.");
                if (closeAfterSave) CompleteAndClose();
                else NotifySettingsApplied();
                return;
            }
            if (hookChanged && updated.CodexTaskIntegration)
            {
                System.Windows.MessageBox.Show(this, "AI 任务联动已启用。Codex 会在出现 Hook 审核提示时请求信任；其他客户端可调用发布包 tools\\balancepet-task.ps1。之后任务开始、完成或停止都会自动通知桌宠。", "BalancePet", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            if (string.IsNullOrWhiteSpace(selectedToken))
            {
                MessageText.Foreground = System.Windows.Media.Brushes.DarkOrange;
                MessageText.Text = "设置已保存；当前账户未填写访问令牌，已跳过余额 API 测试。";
                CompleteAndClose();
                return;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var snapshot = await new JsonBalanceProvider(client).FetchWithRetryAsync(selected, selectedToken);
                MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                var resolved = selected.PresetId == BalancePresetCatalog.Auto && !string.IsNullOrWhiteSpace(snapshot.ResolvedPresetId)
                    ? $"；已识别为 {BalancePresetCatalog.DisplayName(snapshot.ResolvedPresetId, updated.Language)}"
                    : "";
                MessageText.Text = $"连接成功：{snapshot.Amount:0.00} {snapshot.Currency}{resolved}";
                CompleteAndClose();
            }
            catch (Exception error)
            {
                MessageText.Foreground = System.Windows.Media.Brushes.DarkOrange;
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

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange) return;
        SyncComboDisplay(LanguageBox);
        var language = SelectedTag(LanguageBox, "zh-CN");
        AppLocalization.Apply(this, language);
        RefreshLanguageSelector(language, selectLanguage: false);
        SyncAllComboDisplays();
        UpdatePetStyleAvailability();
        UpdatePresetUi(false);
        RefreshExtensionList();
        RefreshExtensionActionLabels(language);
        RefreshPluginCatalogLabels(language);
        RebuildPluginCatalogItems();
    }
    private void CompleteAndClose()
    {
        NotifySettingsApplied();
        Close();
    }

    private void NotifySettingsApplied()
    {
        SettingsApplied = true;
        SettingsAppliedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
