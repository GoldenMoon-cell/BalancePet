using System.Linq;
using System.Windows;
using BalancePet.Wpf.Services;

namespace BalancePet.Wpf;

public partial class UsageWindow : Window
{
    public UsageWindow(UsageLedgerStore store)
    {
        InitializeComponent();
        var days = store.GetRecentHistory();
        HistoryList.ItemsSource = days;
        var total = days.Sum(day => day.Usage);
        var currency = days.FirstOrDefault()?.Currency ?? "USD";
        SummaryText.Text = $"共 {days.Count} 天 · 合计 {total:0.00} {currency}";
    }
}
