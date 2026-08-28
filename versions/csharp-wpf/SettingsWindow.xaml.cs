using System.Globalization;
using System.Net.Http;
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

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RefreshBox.Text, out var refresh) || refresh < 30) { MessageText.Text = "刷新秒数必须是 30 或更大。"; return; }
        if (!double.TryParse(ThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)) { MessageText.Text = "低余额阈值格式不正确。"; return; }
        try
        {
            var token = TokenBox.Password;
            if (string.IsNullOrWhiteSpace(token)) token = _tokens.Unprotect(_settings.TokenBlob);
            var startupEnabled = StartupBox.IsChecked == true;
            var updated = new PetSettings { Endpoint = EndpointBox.Text.Trim(), AuthMode = (AuthModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bearer", HeaderName = HeaderBox.Text.Trim(), TokenBlob = _tokens.Protect(token), BalancePath = PathBox.Text.Trim(), Currency = CurrencyBox.Text.Trim().ToUpperInvariant(), RefreshSeconds = refresh, LowThreshold = threshold, PetStyle = (PetStyleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "deepseek", InteractionMode = (InteractionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "free", Scale = ScaleSlider.Value, Volume = VolumeSlider.Value, Sound = SoundBox.IsChecked == true, Bubble = BubbleBox.IsChecked == true, FollowCodex = FollowCodexBox.IsChecked == true, SystemNotifications = NotificationsBox.IsChecked == true, StartWithWindows = startupEnabled, WindowX = _settings.WindowX, WindowY = _settings.WindowY, Flipped = _settings.Flipped };
            var snapshot = await new JsonBalanceProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }).FetchAsync(updated, token);
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
    }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
