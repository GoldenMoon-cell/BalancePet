namespace BalancePet.Wpf.Models;

public sealed record UsageObservation(
    double Balance,
    double Spent,
    double TodayUsage,
    string Currency,
    DateTimeOffset RecordedAt);

public sealed record UsageDay(string Date, string Currency, double Usage)
{
    public string UsageText => $"{Usage:0.00} {Currency}";
}
