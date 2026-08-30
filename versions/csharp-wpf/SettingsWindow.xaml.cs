using System.Globalization;
using System.IO;
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

    public SettingsWindow(SettingsStore store, DpapiTokenStore tokens, PetSettings settings)
    {
        InitializeComponent(); _store = store; _tokens = tokens; _settings = settings;
        EndpointBox.Text = settings.Endpoint; HeaderBox.Text = settings.HeaderName; PathBox.Text = settings.BalancePath; CurrencyBox.Text = settings.Currency;
        RefreshBox.Text = settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture); ThresholdBox.Text = settings.LowThreshold.ToString(CultureInfo.InvariantCulture);
        SelectByTag(AuthModeBox, settings.AuthMode); SelectByTag(PetStyleBox, settings.PetStyle); SelectByTag(InteractionBox, settings.InteractionMode);
        ScaleSlider.Value = Math.Clamp(settings.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(settings.Volume, 0, 1); SoundBox.IsChecked = settings.Sound; BubbleBox.IsChecked = settings.Bubble; FollowCodexBox.IsChecked = settings.FollowCodex; NotificationsBox.IsChecked = settings.SystemNotifications; StartupBox.IsChecked = settings.StartWithWindows || StartupManager.IsEnabled();
        OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
    }

    private static void SelectByTag(System.Windows.Controls.ComboBox box, string tag)
    { foreach (ComboBoxItem item in box.Items) if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; } box.SelectedIndex = 0; }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "BalancePet 设置 (*.json)|*.json|所有文件 (*.*)|*.*", Title = "导入 BalancePet 设置" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(dialog.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported is null) throw new InvalidDataException("设置文件为空或格式不正确。");
            EndpointBox.Text = imported.Endpoint;
            HeaderBox.Text = imported.HeaderName;
            PathBox.Text = imported.BalancePath;
            CurrencyBox.Text = imported.Currency;
            RefreshBox.Text = Math.Max(30, imported.RefreshSeconds).ToString(CultureInfo.InvariantCulture);
            ThresholdBox.Text = imported.LowThreshold.ToString(CultureInfo.InvariantCulture);
            SelectByTag(AuthModeBox, imported.AuthMode); SelectByTag(PetStyleBox, imported.PetStyle); SelectByTag(InteractionBox, imported.InteractionMode);
            ScaleSlider.Value = Math.Clamp(imported.Scale, 0.6, 1.4); VolumeSlider.Value = Math.Clamp(imported.Volume, 0, 1);
            SoundBox.IsChecked = imported.Sound; BubbleBox.IsChecked = imported.Bubble; FollowCodexBox.IsChecked = imported.FollowCodex; NotificationsBox.IsChecked = imported.SystemNotifications; StartupBox.IsChecked = imported.StartWithWindows;
            OnAuthModeChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
            TokenBox.Clear();
            MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen;
            MessageText.Text = "设置已导入。令牌不会从文件导入，请确认当前令牌仍适用于该中转站。";
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
            var export = new
            {
                endpoint = EndpointBox.Text.Trim(),
                auth_mode = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bearer",
                header_name = HeaderBox.Text.Trim(),
                balance_path = PathBox.Text.Trim(),
                currency = CurrencyBox.Text.Trim().ToUpperInvariant(),
                refresh_seconds = int.TryParse(RefreshBox.Text, out var refresh) ? Math.Max(30, refresh) : 60,
                low_threshold = double.TryParse(ThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) ? threshold : 5,
                pet_style = (PetStyleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "deepseek",
                interaction_mode = (InteractionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "free",
                pet_scale = ScaleSlider.Value,
                sound = SoundBox.IsChecked == true,
                volume = VolumeSlider.Value,
                bubble = BubbleBox.IsChecked == true,
                follow_codex = FollowCodexBox.IsChecked == true,
                system_notifications = NotificationsBox.IsChecked == true,
                start_with_windows = StartupBox.IsChecked == true
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
        if (!int.TryParse(RefreshBox.Text, out var refresh) || refresh < 30) { MessageText.Text = "刷新秒数必须是 30 或更大。"; return; }
        if (!double.TryParse(ThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)) { MessageText.Text = "低余额阈值格式不正确。"; return; }
        var endpointText = EndpointBox.Text.Trim();
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https")) { MessageText.Text = "接口地址必须是 http 或 https 的完整 URL。"; return; }
        var authMode = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bearer";
        if (authMode == "custom" && string.IsNullOrWhiteSpace(HeaderBox.Text)) { MessageText.Text = "使用自定义 Header 时必须填写 Header 名。"; return; }
        try
        {
            var token = TokenBox.Password;
            if (string.IsNullOrWhiteSpace(token)) token = _tokens.Unprotect(_settings.TokenBlob);
            var startupEnabled = StartupBox.IsChecked == true;
            var updated = new PetSettings { Endpoint = endpoint.ToString(), AuthMode = authMode, HeaderName = HeaderBox.Text.Trim(), TokenBlob = _tokens.Protect(token), BalancePath = PathBox.Text.Trim(), Currency = CurrencyBox.Text.Trim().ToUpperInvariant(), RefreshSeconds = refresh, LowThreshold = threshold, PetStyle = (PetStyleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "deepseek", InteractionMode = (InteractionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "free", Scale = ScaleSlider.Value, Volume = VolumeSlider.Value, Sound = SoundBox.IsChecked == true, Bubble = BubbleBox.IsChecked == true, FollowCodex = FollowCodexBox.IsChecked == true, SystemNotifications = NotificationsBox.IsChecked == true, StartWithWindows = startupEnabled, WindowX = _settings.WindowX, WindowY = _settings.WindowY, Flipped = _settings.Flipped };
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var snapshot = await new JsonBalanceProvider(client).FetchWithRetryAsync(updated, token);
            _store.Save(updated);
            if (!StartupManager.SetEnabled(startupEnabled))
            {
                MessageText.Foreground = System.Windows.Media.Brushes.DarkOrange;
                MessageText.Text = "连接成功，但开机启动项写入失败，请检查 Windows 权限。";
                return;
            }
            MessageText.Foreground = System.Windows.Media.Brushes.SeaGreen; MessageText.Text = $"连接成功：{snapshot.Amount:0.00} {snapshot.Currency}"; DialogResult = true;
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
