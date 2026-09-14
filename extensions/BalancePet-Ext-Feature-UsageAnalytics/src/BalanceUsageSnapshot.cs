using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.UsageAnalytics;

/// <summary>
/// Reads the host's credential-free balance-change summary. This is separate
/// from Usage.v1 because it describes account balance observations, not an LLM
/// request's token counters.
/// </summary>
public sealed class BalanceUsageSnapshot
{
    public const string Schema = "balancepet.balance-usage.v1";
    public const string FileName = "balance-usage.v1.json";

    public bool HasData { get; private init; }
    public bool HasBalanceData { get; private init; }
    public double TodayUsage { get; private init; }
    public double RecentUsage { get; private init; }
    public double TotalBalance { get; private init; }
    public string Currency { get; private init; } = "USD";
    public string BalanceCurrency { get; private init; } = "USD";
    public string AccountId { get; private init; } = "";
    public int BalanceAccountCount { get; private init; }

    public static BalanceUsageSnapshot Read(string directory, DateTimeOffset? now = null)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path)) return Empty();
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            if (document is null || document.Schema != Schema || document.Entries is null) return Empty();

            var selected = document.SelectedAccountId ?? "";
            var entries = document.Entries
                .Where(entry => entry is not null && IsValid(entry))
                .Where(entry => string.IsNullOrWhiteSpace(selected) || entry!.AccountId == selected)
                .ToArray();
            var today = (now ?? DateTimeOffset.Now).ToString("yyyy-MM-dd");
            var todayEntries = entries.Where(entry => entry!.Date == today).ToArray();
            var latest = todayEntries.FirstOrDefault() ?? entries.OrderByDescending(entry => entry!.Date, StringComparer.Ordinal).FirstOrDefault();
            var currentToday = todayEntries.Sum(entry => entry!.Usage);
            var recentFrom = (now ?? DateTimeOffset.Now).Date.AddDays(-29);
            var recent = entries.Where(entry => DateTime.TryParseExact(entry!.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date) && date >= recentFrom)
                .Sum(entry => entry!.Usage);
            var balances = (document.Balances ?? Array.Empty<BalanceEntry>())
                .Where(IsValid)
                .ToArray();
            var selectedBalance = balances.FirstOrDefault(entry => string.Equals(entry.AccountId, selected, StringComparison.OrdinalIgnoreCase));
            var balanceGroups = balances.GroupBy(entry => entry.Currency, StringComparer.OrdinalIgnoreCase).ToArray();
            var commonCurrency = selectedBalance?.Currency ?? (balanceGroups.Length == 1 ? balanceGroups[0].Key : "USD");
            var totalBalance = balanceGroups.Length == 1
                ? balanceGroups[0].Sum(entry => entry.Amount)
                : selectedBalance?.Amount ?? 0;
            return new BalanceUsageSnapshot
            {
                HasData = todayEntries.Length > 0,
                HasBalanceData = balances.Length > 0,
                TodayUsage = currentToday,
                RecentUsage = recent,
                TotalBalance = totalBalance,
                Currency = latest?.Currency ?? selectedBalance?.Currency ?? "USD",
                BalanceCurrency = commonCurrency,
                AccountId = latest?.AccountId ?? selectedBalance?.AccountId ?? "",
                BalanceAccountCount = balanceGroups.Length == 1 ? balances.Length : selectedBalance is null ? 0 : 1
            };
        }
        catch (IOException) { return Empty(); }
        catch (UnauthorizedAccessException) { return Empty(); }
        catch (JsonException) { return Empty(); }
    }

    private static bool IsValid(Entry? entry)
        => entry is not null
            && !string.IsNullOrWhiteSpace(entry.AccountId)
            && entry.AccountId.Length <= 128
            && !string.IsNullOrWhiteSpace(entry.Date)
            && DateTime.TryParseExact(entry.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _)
            && !string.IsNullOrWhiteSpace(entry.Currency)
            && entry.Currency.Length <= 12
            && double.IsFinite(entry.Usage)
            && entry.Usage >= 0
            && entry.Usage <= 10_000_000_000;

    private static bool IsValid(BalanceEntry? entry)
        => entry is not null
            && !string.IsNullOrWhiteSpace(entry.AccountId)
            && entry.AccountId.Length <= 128
            && !string.IsNullOrWhiteSpace(entry.Currency)
            && entry.Currency.Length <= 12
            && double.IsFinite(entry.Amount)
            && entry.Amount >= -10_000_000_000
            && entry.Amount <= 10_000_000_000;

    private static BalanceUsageSnapshot Empty() => new() { HasData = false };

    private sealed class Document
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "";
        [JsonPropertyName("selected_account_id")] public string? SelectedAccountId { get; set; }
        [JsonPropertyName("entries")] public Entry[]? Entries { get; set; }
        [JsonPropertyName("balances")] public BalanceEntry[]? Balances { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("usage")] public double Usage { get; set; }
    }

    private sealed class BalanceEntry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("amount")] public double Amount { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}
