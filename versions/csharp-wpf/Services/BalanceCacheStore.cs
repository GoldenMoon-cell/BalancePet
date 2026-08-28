using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Persists the last successful balance only. Credentials are never written here.
/// </summary>
public sealed class BalanceCacheStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "csharp-balance-cache.json");

    public void Save(BalanceSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CacheEntry
            {
                Amount = snapshot.Amount,
                Currency = snapshot.Currency,
                UpdatedAt = snapshot.UpdatedAt
            }));
            File.Move(temporary, _path, true);
        }
        catch (IOException) { }
    }

    public bool TryLoad(out BalanceSnapshot snapshot)
    {
        try
        {
            if (File.Exists(_path))
            {
                var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(_path));
                if (entry is not null && double.IsFinite(entry.Amount) && !string.IsNullOrWhiteSpace(entry.Currency))
                {
                    snapshot = new BalanceSnapshot(entry.Amount, entry.Currency, entry.UpdatedAt);
                    return true;
                }
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        snapshot = default!;
        return false;
    }

    private sealed class CacheEntry
    {
        [JsonPropertyName("amount")] public double Amount { get; set; }
        [JsonPropertyName("currency")] public string Currency { get; set; } = "USD";
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    }
}
