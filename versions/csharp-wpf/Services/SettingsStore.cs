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
            if (File.Exists(_path))
            {
                var settings = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(_path), Options) ?? new PetSettings();
                EnsureMonitorProfiles(settings);
                return settings;
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        var defaults = new PetSettings();
        EnsureMonitorProfiles(defaults);
        return defaults;
    }

    public void Save(PetSettings settings)
    {
        EnsureMonitorProfiles(settings);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, _path, true);
    }

    private static void EnsureMonitorProfiles(PetSettings settings)
    {
        settings.Monitors ??= new List<MonitorProfile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in settings.Monitors)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !seen.Add(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "监控账户" : profile.Name.Trim();
            profile.RefreshSeconds = Math.Max(30, profile.RefreshSeconds);
            profile.Currency = string.IsNullOrWhiteSpace(profile.Currency) ? "USD" : profile.Currency.Trim().ToUpperInvariant();
        }

        if (settings.Monitors.Count == 0)
        {
            var migrated = new MonitorProfile
            {
                Id = "default",
                Name = "默认账户",
                Endpoint = settings.Endpoint,
                AuthMode = settings.AuthMode,
                HeaderName = settings.HeaderName,
                TokenBlob = settings.TokenBlob,
                BalancePath = settings.BalancePath,
                Currency = settings.Currency,
                RefreshSeconds = Math.Max(30, settings.RefreshSeconds),
                AutoRefreshEnabled = settings.AutoRefreshEnabled,
                LowThreshold = settings.LowThreshold,
                Enabled = true
            };
            settings.Monitors.Add(migrated);
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedMonitorId) || !settings.Monitors.Any(p => string.Equals(p.Id, settings.SelectedMonitorId, StringComparison.OrdinalIgnoreCase)))
            settings.SelectedMonitorId = settings.Monitors[0].Id;

        var selected = settings.Monitors.FirstOrDefault(p => string.Equals(p.Id, settings.SelectedMonitorId, StringComparison.OrdinalIgnoreCase)) ?? settings.Monitors[0];
        settings.Endpoint = selected.Endpoint;
        settings.AuthMode = selected.AuthMode;
        settings.HeaderName = selected.HeaderName;
        settings.TokenBlob = selected.TokenBlob;
        settings.BalancePath = selected.BalancePath;
        settings.Currency = selected.Currency;
        settings.RefreshSeconds = selected.RefreshSeconds;
        settings.AutoRefreshEnabled = selected.AutoRefreshEnabled;
        settings.LowThreshold = selected.LowThreshold;
    }
}
