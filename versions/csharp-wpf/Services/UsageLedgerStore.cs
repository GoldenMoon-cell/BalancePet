using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Stores only balance observations, never provider credentials.
/// </summary>
public sealed class UsageLedgerStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public UsageLedgerStore()
    {
        _path = BuildPath("default");
    }

    public UsageLedgerStore(string profileId)
    {
        _path = BuildPath(profileId);
    }

    private static string BuildPath(string profileId)
    {
        var safeId = string.IsNullOrWhiteSpace(profileId) ? "default" : profileId.Trim();
        var fileName = string.Equals(safeId, "default", StringComparison.OrdinalIgnoreCase)
            ? "csharp-usage-ledger.json"
            : $"csharp-usage-ledger-{safeId}.json";
        safeId = new string(safeId.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_').ToArray());
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BalancePet");
        return Path.Combine(directory, string.Equals(fileName, "csharp-usage-ledger.json", StringComparison.Ordinal) ? fileName : $"csharp-usage-ledger-{safeId}.json");
    }

    public UsageObservation Record(double balance, string currency, DateTimeOffset? now = null)
    {
        var recordedAt = now ?? DateTimeOffset.Now;
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var ledger = Load();
        var today = recordedAt.ToString("yyyy-MM-dd");
        var spent = 0d;

        if (!string.Equals(ledger.Date, today, StringComparison.Ordinal) ||
            !string.Equals(ledger.Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(ledger.Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                ledger.History.Clear();
            }
            else if (!string.IsNullOrWhiteSpace(ledger.Date))
            {
                ledger.History[ledger.Date] = Math.Max(0, ledger.Usage);
                TrimHistory(ledger.History);
            }
            ledger = new Ledger
            {
                Date = today,
                Currency = normalizedCurrency,
                Usage = 0,
                Balance = null,
                History = ledger.History
            };
        }
        else if (ledger.Balance.HasValue && balance < ledger.Balance.Value)
        {
            spent = ledger.Balance.Value - balance;
            ledger.Usage += spent;
        }

        ledger.Date = today;
        ledger.Currency = normalizedCurrency;
        ledger.Balance = balance;
        Save(ledger);
        return new UsageObservation(balance, spent, Math.Max(0, ledger.Usage), normalizedCurrency, recordedAt);
    }

    public IReadOnlyList<UsageDay> GetRecentHistory(int days = 30)
    {
        var ledger = Load();
        var limit = Math.Clamp(days, 1, 90);
        var history = ledger.History
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new UsageDay(pair.Key, string.IsNullOrWhiteSpace(ledger.Currency) ? "USD" : ledger.Currency, Math.Max(0, pair.Value)))
            .ToList();
        if (!string.IsNullOrWhiteSpace(ledger.Date))
        {
            history.RemoveAll(day => string.Equals(day.Date, ledger.Date, StringComparison.Ordinal));
            history.Add(new UsageDay(ledger.Date, string.IsNullOrWhiteSpace(ledger.Currency) ? "USD" : ledger.Currency, Math.Max(0, ledger.Usage)));
        }
        return history
            .OrderByDescending(day => day.Date, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private Ledger Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var ledger = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(_path), Options) ?? new Ledger();
                ledger.History ??= new Dictionary<string, double>(StringComparer.Ordinal);
                return ledger;
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Ledger();
    }

    private void Save(Ledger ledger)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, Options));
            File.Move(temporary, _path, true);
        }
        catch (IOException) { }
    }

    private static void TrimHistory(Dictionary<string, double> history)
    {
        foreach (var date in history.Keys.OrderByDescending(value => value, StringComparer.Ordinal).Skip(30).ToArray())
        {
            history.Remove(date);
        }
    }

    private sealed class Ledger
    {
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "";
        [JsonPropertyName("balance")] public double? Balance { get; set; }
        [JsonPropertyName("usage")] public double Usage { get; set; }
        [JsonPropertyName("history")] public Dictionary<string, double> History { get; set; } = new(StringComparer.Ordinal);
    }
}
