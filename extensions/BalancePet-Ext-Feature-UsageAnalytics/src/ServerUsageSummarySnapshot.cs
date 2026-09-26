using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.UsageAnalytics;

public sealed class ServerUsageSummarySnapshot
{
    public const string Schema = "balancepet.server-usage.v1";
    public const string FileName = "server-usage.v1.json";

    public bool HasData { get; private init; }
    public double TodayAmount { get; private init; }
    public string Currency { get; private init; } = "USD";
    public string Date { get; private init; } = "";

    public static ServerUsageSummarySnapshot Read(string directory, DateTimeOffset? now = null)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return Empty();
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            if (document is null || document.Schema != Schema || document.Entries is null) return Empty();

            var selected = document.SelectedAccountId ?? "";
            var entries = document.Entries
                .Where(entry => entry is not null && IsValid(entry))
                .Where(entry => string.IsNullOrWhiteSpace(selected) || entry!.AccountId == selected)
                .ToArray();
            var today = (now ?? DateTimeOffset.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var latest = entries.FirstOrDefault(entry => entry!.Date == today)
                ?? entries.OrderByDescending(entry => entry!.Date, StringComparer.Ordinal).FirstOrDefault();
            return latest is null
                ? Empty()
                : new ServerUsageSummarySnapshot
                {
                    HasData = true,
                    TodayAmount = latest.Amount,
                    Currency = latest.Currency,
                    Date = latest.Date
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
            && DateTime.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && !string.IsNullOrWhiteSpace(entry.Currency)
            && entry.Currency.Length <= 12
            && double.IsFinite(entry.Amount)
            && entry.Amount >= 0
            && entry.Amount <= 10_000_000_000;

    private static ServerUsageSummarySnapshot Empty() => new();

    private sealed class Document
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "";
        [JsonPropertyName("selected_account_id")] public string? SelectedAccountId { get; set; }
        [JsonPropertyName("entries")] public Entry[]? Entries { get; set; }
    }

    private sealed class Entry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("amount")] public double Amount { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}
