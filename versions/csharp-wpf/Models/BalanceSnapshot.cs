namespace BalancePet.Wpf.Models;

public sealed record BalanceSnapshot(double Amount, string Currency, DateTimeOffset UpdatedAt, string? ResolvedPresetId = null);
