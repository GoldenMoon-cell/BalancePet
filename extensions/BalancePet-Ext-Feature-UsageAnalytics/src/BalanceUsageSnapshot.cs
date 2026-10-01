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
    public bool HasTotalUsageData { get; private init; }
    public double TodayUsage { get; private init; }
    public double RecentUsage { get; private init; }
    public double TotalUsage { get; private init; }
    public double TotalBalance { get; private init; }
    public string Currency { get; private init; } = "USD";
    public string BalanceCurrency { get; private init; } = "USD";
    public string AccountId { get; private init; } = "";
    public int BalanceAccountCount { get; private init; }
    /// <summary>Every account the host reported, in the order the file lists them.</summary>
    public IReadOnlyList<string> Accounts { get; private init; } = Array.Empty<string>();

    /// <param name="accountId">
    /// Restricts every figure to one account. Empty — the default — means all
    /// accounts, so the balance, usage and today figures always describe the
    /// same scope instead of mixing an all-account balance with one account's
    /// usage.
    /// </param>
    public static BalanceUsageSnapshot Read(string directory, DateTimeOffset? now = null, string accountId = "")
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path)) return Empty();
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            if (document is null || document.Schema != Schema || document.Entries is null) return Empty();

            var selected = accountId ?? "";
            bool Wanted(string? id) => string.IsNullOrWhiteSpace(selected)
                || string.Equals(id, selected, StringComparison.OrdinalIgnoreCase);

            var allBalances = (document.Balances ?? Array.Empty<BalanceEntry>()).Where(IsValid).ToArray();
            var accounts = allBalances.Select(entry => entry!.AccountId)
                .Concat((document.Totals ?? Array.Empty<TotalEntry>()).Where(IsValid).Select(entry => entry!.AccountId))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var entries = document.Entries
                .Where(entry => entry is not null && IsValid(entry) && Wanted(entry!.AccountId))
                .ToArray();
            var today = (now ?? DateTimeOffset.Now).ToString("yyyy-MM-dd");
            var todayEntries = entries.Where(entry => entry!.Date == today).ToArray();
            var latest = todayEntries.FirstOrDefault() ?? entries.OrderByDescending(entry => entry!.Date, StringComparer.Ordinal).FirstOrDefault();
            var currentToday = todayEntries.Sum(entry => entry!.Usage);
            var recentFrom = (now ?? DateTimeOffset.Now).Date.AddDays(-29);
            var recent = entries.Where(entry => DateTime.TryParseExact(entry!.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date) && date >= recentFrom)
                .Sum(entry => entry!.Usage);
            var balances = allBalances.Where(entry => Wanted(entry!.AccountId)).ToArray();
            var totals = (document.Totals ?? Array.Empty<TotalEntry>())
                .Where(IsValid)
                .Where(entry => Wanted(entry!.AccountId))
                .ToArray();
            var totalGroups = totals.GroupBy(entry => entry.Currency, StringComparer.OrdinalIgnoreCase).ToArray();
            var selectedTotal = totals.FirstOrDefault(entry => string.Equals(entry.AccountId, selected, StringComparison.OrdinalIgnoreCase));
            var hasTotalUsage = totals.Length > 0 && (totalGroups.Length == 1 || selectedTotal is not null);
            var totalUsage = hasTotalUsage
                ? totalGroups.Length == 1 ? totalGroups[0].Sum(entry => entry.Amount) : selectedTotal!.Amount
                : 0;
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
                HasTotalUsageData = hasTotalUsage,
                TodayUsage = currentToday,
                RecentUsage = recent,
                TotalUsage = totalUsage,
                TotalBalance = totalBalance,
                Currency = latest?.Currency ?? selectedBalance?.Currency ?? "USD",
                BalanceCurrency = commonCurrency,
                AccountId = latest?.AccountId ?? selectedBalance?.AccountId ?? "",
                Accounts = accounts,
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

    private static bool IsValid(TotalEntry? entry)
        => entry is not null
            && !string.IsNullOrWhiteSpace(entry.AccountId)
            && entry.AccountId.Length <= 128
            && !string.IsNullOrWhiteSpace(entry.Currency)
            && entry.Currency.Length <= 12
            && double.IsFinite(entry.Amount)
            && entry.Amount >= 0
            && entry.Amount <= 10_000_000_000;

    private static BalanceUsageSnapshot Empty() => new() { HasData = false };

    private sealed class Document
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "";
        [JsonPropertyName("selected_account_id")] public string? SelectedAccountId { get; set; }
        [JsonPropertyName("entries")] public Entry[]? Entries { get; set; }
        [JsonPropertyName("balances")] public BalanceEntry[]? Balances { get; set; }
        [JsonPropertyName("totals")] public TotalEntry[]? Totals { get; set; }
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

    private sealed class TotalEntry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("amount")] public double Amount { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}
