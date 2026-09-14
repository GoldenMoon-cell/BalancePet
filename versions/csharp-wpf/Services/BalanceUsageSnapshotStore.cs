using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Publishes the host's local balance-change ledger as a small, versioned,
/// credential-free file for out-of-process feature extensions.
/// </summary>
public sealed class BalanceUsageSnapshotStore
{
    public const string Schema = "balancepet.balance-usage.v1";
    public const string FileName = "balance-usage.v1.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly object _gate = new();
    private readonly string _path;

    public BalanceUsageSnapshotStore(string? directory = null)
    {
        var root = string.IsNullOrWhiteSpace(directory)
            ? UsageEventBridge.GetDefaultDirectory()
            : directory;
        _path = Path.Combine(root, FileName);
    }

    public static string GetDefaultPath() => Path.Combine(UsageEventBridge.GetDefaultDirectory(), FileName);

    public void Publish(IEnumerable<ProfileSource> profiles, string? selectedAccountId)
    {
        var entries = profiles
            .Where(profile => profile is not null && profile.Ledger is not null)
            .SelectMany(profile => profile.Ledger.GetRecentHistory(30).Select(day => new BalanceUsageEntry
            {
                AccountId = Clean(profile.AccountId, 128),
                Date = day.Date,
                Currency = CleanCurrency(day.Currency),
                Usage = double.IsFinite(day.Usage) ? Math.Clamp(day.Usage, 0, 10_000_000_000) : 0
            }))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.AccountId) &&
                            !string.IsNullOrWhiteSpace(entry.Date) &&
                            !string.IsNullOrWhiteSpace(entry.Currency))
            .OrderBy(entry => entry.AccountId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Date, StringComparer.Ordinal)
            .ToArray();

        var document = new BalanceUsageDocument
        {
            Schema = Schema,
            UpdatedAt = DateTimeOffset.Now,
            SelectedAccountId = Clean(selectedAccountId, 128),
            Entries = entries
        };

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options), new UTF8Encoding(false));
                File.Move(temporary, _path, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public sealed record ProfileSource(string AccountId, UsageLedgerStore Ledger);

    private sealed class BalanceUsageDocument
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = BalanceUsageSnapshotStore.Schema;
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("selected_account_id")] public string SelectedAccountId { get; set; } = "";
        [JsonPropertyName("entries")] public BalanceUsageEntry[] Entries { get; set; } = Array.Empty<BalanceUsageEntry>();
    }

    private sealed class BalanceUsageEntry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("usage")] public double Usage { get; set; }
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }

    private static string CleanCurrency(string? value)
    {
        var currency = Clean(value, 12).ToUpperInvariant();
        return currency.All(character => character is >= 'A' and <= 'Z' or ' ')
            ? currency
            : "USD";
    }
}
