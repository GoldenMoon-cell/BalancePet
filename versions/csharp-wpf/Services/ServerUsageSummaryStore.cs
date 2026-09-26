using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Publishes credential-free aggregate provider usage for feature extensions.
/// </summary>
public sealed class ServerUsageSummaryStore
{
    public const string Schema = "balancepet.server-usage.v1";
    public const string FileName = "server-usage.v1.json";
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", FileName);
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Publish(ServerUsageSummary summary, string? selectedAccountId)
    {
        if (summary.Days.Count == 0 || string.IsNullOrWhiteSpace(summary.AccountId)) return;
        lock (_gate)
        {
            try
            {
                var document = Load();
                var entries = (document.Entries ?? Array.Empty<Entry>())
                    .Where(entry => !string.Equals(entry.AccountId, summary.AccountId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                entries.AddRange(summary.Days
                    .Where(day => IsValidDate(day.Date) && double.IsFinite(day.Amount) && day.Amount >= 0)
                    .Select(day => new Entry
                    {
                        AccountId = Clean(summary.AccountId, 128),
                        Date = day.Date,
                        Currency = Clean(summary.Currency, 12).ToUpperInvariant(),
                        Amount = Math.Clamp(day.Amount, 0, 10_000_000_000)
                    }));
                entries = entries
                    .GroupBy(entry => $"{entry.AccountId}\u001f{entry.Date}", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .OrderBy(entry => entry.AccountId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Date, StringComparer.Ordinal)
                    .TakeLast(120)
                    .ToList();

                var output = new Document
                {
                    Schema = Schema,
                    UpdatedAt = summary.UpdatedAt,
                    SelectedAccountId = Clean(selectedAccountId, 128),
                    Entries = entries.ToArray()
                };
                Save(output);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public void SetSelectedAccount(string? selectedAccountId)
    {
        lock (_gate)
        {
            try
            {
                var document = Load();
                document.Schema = Schema;
                document.SelectedAccountId = Clean(selectedAccountId, 128);
                document.UpdatedAt = DateTimeOffset.Now;
                Save(document);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private Document Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), Options) ?? new Document();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Document();
    }

    private void Save(Document document)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, Options));
        File.Move(temporary, _path, true);
    }

    private static bool IsValidDate(string value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var filtered = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return filtered.Length <= maxLength ? filtered : filtered[..maxLength];
    }

    private sealed class Document
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = ServerUsageSummaryStore.Schema;
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("selected_account_id")] public string SelectedAccountId { get; set; } = "";
        [JsonPropertyName("entries")] public Entry[] Entries { get; set; } = Array.Empty<Entry>();
    }

    private sealed class Entry
    {
        [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("amount")] public double Amount { get; set; }
    }
}
