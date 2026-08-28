using System.IO;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

public sealed class SettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public SettingsStore()
    {
        var configured = Environment.GetEnvironmentVariable("BALANCEPET_CSHARP_CONFIG");
        _path = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet", "csharp-settings.json");
    }

    public string Path => _path;

    public PetSettings Load()
    {
        try
        {
            if (File.Exists(_path)) return JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(_path), Options) ?? new PetSettings();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new PetSettings();
    }

    public void Save(PetSettings settings)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, _path, true);
    }
}
