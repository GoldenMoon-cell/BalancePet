using System.Linq;
using System.Windows;
using BalancePet.Wpf.Services;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf;

public partial class UsageWindow : Window
{
    public UsageWindow(UsageLedgerStore store, PetSettings settings)
    {
        InitializeComponent();
        WindowThemeService.ApplyConfiguredTheme(this, settings);
        var language = settings.Language;
        AppLocalization.Apply(this, language);
        var days = store.GetRecentHistory();
        HistoryList.ItemsSource = days;
        var total = days.Sum(day => day.Usage);
        var currency = days.FirstOrDefault()?.Currency ?? "USD";
        SummaryText.Text = AppLocalization.IsEnglish(language)
            ? $"{days.Count} days · Total {total:0.00} {currency}"
            : $"共 {days.Count} 天 · 合计 {total:0.00} {currency}";
    }
}
