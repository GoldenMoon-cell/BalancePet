using System.Globalization;
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
    private readonly SettingsStore _store;
    private readonly DpapiTokenStore _tokens;
    private readonly PetSettings _settings;
    private readonly List<MonitorProfile> _profiles;
    private string _currentProfileId = "";
    private bool _suppressProfileChange;
    private bool _suppressRefreshChange;

    public SettingsWindow(SettingsStore store, DpapiTokenStore tokens, PetSettings settings)
    {
        InitializeComponent(); _store = store; _tokens = tokens; _settings = settings;
        _profiles = settings.Monitors is { Count: > 0 }
            ? settings.Monitors.Select(CloneProfile).ToList()
            : new List<MonitorProfile> { CreateProfileFromLegacy(settings) };
        RefreshProfileList(settings.SelectedMonitorId);
        SelectByTag(PetStyleBox, settings.PetStyle); SelectByTag(InteractionBox, settings.InteractionMode); SelectByTag(UpdateCheckBox, settings.UpdateCheckMode);
        ScaleSlider.Value = Math.Clamp(settings.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(settings.Volume, 0, 1); SoundBox.IsChecked = settings.Sound; BubbleBox.IsChecked = settings.Bubble; InteractionEffectsBox.IsChecked = settings.InteractionEffects; EasterEggsBox.IsChecked = settings.RandomEasterEggs; FollowCodexBox.IsChecked = settings.CodexTaskIntegration; NotificationsBox.IsChecked = settings.SystemNotifications; StartupBox.IsChecked = settings.StartWithWindows || StartupManager.IsEnabled();
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox box, string tag)
    { foreach (ComboBoxItem item in box.Items) if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; } box.SelectedIndex = 0; }

    private static MonitorProfile CreateProfileFromLegacy(PetSettings settings) => new()
    {
        Id = "default",
        Name = "默认账户",
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
    }

    private void SaveCurrentProfileFields()
    {
        var profile = CurrentProfile;
        if (profile is null) return;
        profile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "监控账户" : ProfileNameBox.Text.Trim();
        profile.Endpoint = EndpointBox.Text.Trim();
        profile.AuthMode = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bearer";
        profile.HeaderName = HeaderBox.Text.Trim();
        profile.BalancePath = PathBox.Text.Trim();
        profile.Currency = string.IsNullOrWhiteSpace(CurrencyBox.Text) ? "USD" : CurrencyBox.Text.Trim().ToUpperInvariant();
        var refreshTag = (RefreshBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
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

    private void OnRefreshModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRefreshChange) return;
        UpdateRefreshModeVisibility();
    }

    private void UpdateRefreshModeVisibility()
    {
        var custom = string.Equals((RefreshBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "custom", StringComparison.OrdinalIgnoreCase);
        RefreshCustomBox.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool ValidateRefreshInput()
    {
        var tag = (RefreshBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
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
        if (_suppressProfileChange || ProfileBox.SelectedItem is not ComboBoxItem item) return;
        SaveCurrentProfileFields();
        LoadProfile(item.Tag?.ToString() ?? "");
    }

    private void OnAddProfile(object sender, RoutedEventArgs e)
    {
        SaveCurrentProfileFields();
        var profile = new MonitorProfile { Id = Guid.NewGuid().ToString("N"), Name = $"监控账户 {_profiles.Count + 1}", Endpoint = "", Enabled = true };
        _profiles.Add(profile);
        RefreshProfileList(profile.Id);
        EndpointBox.Focus();
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
            SelectByTag(PetStyleBox, imported.PetStyle); SelectByTag(InteractionBox, imported.InteractionMode); SelectByTag(UpdateCheckBox, imported.UpdateCheckMode);
            ScaleSlider.Value = Math.Clamp(imported.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(imported.Volume, 0, 1);
            SoundBox.IsChecked = imported.Sound; BubbleBox.IsChecked = imported.Bubble; InteractionEffectsBox.IsChecked = imported.InteractionEffects; EasterEggsBox.IsChecked = imported.RandomEasterEggs; NotificationsBox.IsChecked = imported.SystemNotifications; StartupBox.IsChecked = imported.StartWithWindows;
            OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
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
                auth_mode = selected.AuthMode,
                header_name = selected.HeaderName,
                balance_path = selected.BalancePath,
                currency = selected.Currency,
                refresh_seconds = selected.RefreshSeconds,
                auto_refresh_enabled = selected.AutoRefreshEnabled,
                low_threshold = selected.LowThreshold,
                pet_style = (PetStyleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "deepseek",
                interaction_mode = (InteractionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "free",
                update_check_mode = (UpdateCheckBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "daily",
                pet_scale = ScaleSlider.Value,
                sound = SoundBox.IsChecked == true,
                volume = VolumeSlider.Value,
                bubble = BubbleBox.IsChecked == true,
                interaction_effects = InteractionEffectsBox.IsChecked == true,
                random_easter_eggs = EasterEggsBox.IsChecked == true,
                codex_task_integration = FollowCodexBox.IsChecked == true,
                system_notifications = NotificationsBox.IsChecked == true,
                start_with_windows = StartupBox.IsChecked == true,
                selected_monitor_id = selected.Id,
                monitors = _profiles.Select(profile => new
                {
                    id = profile.Id,
                    name = profile.Name,
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

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        SaveCurrentProfileFields();
        if (_profiles.Count == 0) { MessageText.Text = "至少保留一个监控账户。"; return; }
        if (!ValidateRefreshInput()) return;
        foreach (var profile in _profiles.Where(value => value.Enabled))
        {
            if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            {
                MessageText.Text = $"监控账户“{profile.Name}”的接口地址无效。";
                RefreshProfileList(profile.Id);
                return;
            }
            if (string.IsNullOrWhiteSpace(profile.BalancePath)) { MessageText.Text = $"监控账户“{profile.Name}”的余额 JSON 路径不能为空。"; RefreshProfileList(profile.Id); return; }
            if (profile.AuthMode == "custom" && string.IsNullOrWhiteSpace(profile.HeaderName)) { MessageText.Text = $"监控账户“{profile.Name}”使用自定义 Header 时必须填写 Header 名。"; RefreshProfileList(profile.Id); return; }
        }
        var selected = CurrentProfile ?? _profiles[0];
        string selectedToken;
        try
        {
            selectedToken = _tokens.Unprotect(selected.TokenBlob);
        }
        catch (Exception error)
        {
            MessageText.Text = $"当前账户令牌无法解密：{error.Message}";
            return;
        }
        try
        {
            var startupEnabled = StartupBox.IsChecked == true;
            var updated = new PetSettings
            {
                Endpoint = selected.Endpoint,
                AuthMode = selected.AuthMode,
                HeaderName = selected.HeaderName,
                TokenBlob = selected.TokenBlob,
                BalancePath = selected.BalancePath,
                Currency = selected.Currency,
                RefreshSeconds = Math.Max(30, selected.RefreshSeconds),
                AutoRefreshEnabled = selected.AutoRefreshEnabled,
                LowThreshold = selected.LowThreshold,
                PetStyle = (PetStyleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "deepseek",
                InteractionMode = (InteractionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "free",
                UpdateCheckMode = (UpdateCheckBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "daily",
                LastUpdateCheckUtc = _settings.LastUpdateCheckUtc,
                Scale = ScaleSlider.Value,
                Volume = VolumeSlider.Value,
                Sound = SoundBox.IsChecked == true,
                Bubble = BubbleBox.IsChecked == true,
                InteractionEffects = InteractionEffectsBox.IsChecked == true,
                RandomEasterEggs = EasterEggsBox.IsChecked == true,
                CodexTaskIntegration = FollowCodexBox.IsChecked == true,
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
            if (!StartupManager.SetEnabled(startupEnabled))
            {
                MessageText.Foreground = System.Windows.Media.Brushes.DarkOrange;
                MessageText.Text = "设置已保存，但开机启动项写入失败，请检查 Windows 权限。";
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
                DialogResult = true;
                return;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var snapshot = await new JsonBalanceProvider(client).FetchWithRetryAsync(selected, selectedToken);
                MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
                MessageText.Text = $"连接成功：{snapshot.Amount:0.00} {snapshot.Currency}";
                DialogResult = true;
            }
            catch (Exception error)
            {
                MessageText.Foreground = System.Windows.Media.Brushes.DarkOrange;
                var message = $"设置已保存，但余额 API 测试失败：{error.Message}";
                MessageText.Text = message;
                System.Windows.MessageBox.Show(this, message, "BalancePet", MessageBoxButton.OK, MessageBoxImage.Warning);
                DialogResult = true;
            }
        }
        catch (Exception error) { MessageText.Text = error.Message; }
    }

    private void OnAuthModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HeaderBox is null || AuthModeBox is null) return;
        var custom = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "custom";
        HeaderBox.IsEnabled = custom;
        if (!custom) HeaderBox.Text = "Authorization";
        AuthHint.Text = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "bearer" => "令牌框只填写令牌本身，程序会自动发送 Authorization: Bearer <令牌>。",
            "authorization" => "令牌框填写完整 Authorization 值，例如 Bearer sk-...。",
            "websee-session" => "令牌框填写 websee-session 会话值；仅适用于提供该接口格式的中转站。",
            "x-api-key" => "令牌框填写 API key，程序会发送 x-api-key 请求头。",
            "custom" => "令牌框填写 Header 值；上方 Header 名必须与中转站文档完全一致。",
            _ => "请以中转站接口文档要求为准。"
        };
    }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
