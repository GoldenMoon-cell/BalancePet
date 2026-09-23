using System.IO;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

public sealed class SettingsStore
{
    private const int CurrentSchemaVersion = 3;
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
                Normalize(settings);
                return settings;
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        var defaults = new PetSettings();
        Normalize(defaults);
        return defaults;
    }

    public void Save(PetSettings settings)
    {
        Normalize(settings);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, _path, true);
    }

    private static void Normalize(PetSettings settings)
    {
        Migrate(settings);
        NormalizeUpdateModes(settings);
        EnsureMonitorProfiles(settings);
    }

    private static void Migrate(PetSettings settings)
    {
        // Version 1 files predate an explicit schema marker. Their existing
        // account_status_integration key is still mapped to CCSwitchIntegration
        // by the model, so the migration only needs to establish the marker.
        if (settings.SettingsSchemaVersion < 1)
            settings.SettingsSchemaVersion = 1;

        // Version 2 formalizes monitor profiles as the canonical account store.
        // EnsureMonitorProfiles below performs the legacy single-account lift.
        if (settings.SettingsSchemaVersion < 2)
            settings.SettingsSchemaVersion = 2;

        // Version 3 adds declarative UI theme selection. Existing installs
        // receive the bundled Mica theme without changing pet-window behavior.
        if (settings.SettingsSchemaVersion < 3)
        {
            settings.ThemeId = ThemeExtensionManager.BundledThemeId;
            settings.ThemeMode = "system";
            settings.ThemeBackdrop = "mica";
            settings.SettingsSchemaVersion = 3;
        }

        // Never downgrade a file created by a newer build.
        if (settings.SettingsSchemaVersion <= CurrentSchemaVersion)
            settings.SettingsSchemaVersion = CurrentSchemaVersion;
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
            profile.PresetId = BalancePresetCatalog.NormalizeId(profile.PresetId);
            if (BalancePresetCatalog.UsesSiteUrl(profile.PresetId))
                BalancePresetCatalog.Apply(profile, profile.PresetId, BalancePresetCatalog.ResolveSiteUrl(profile));
        }

        if (settings.Monitors.Count == 0)
        {
            var migrated = new MonitorProfile
            {
                Id = "default",
                Name = "默认账户",
                PresetId = BalancePresetCatalog.Custom,
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

    private static void NormalizeUpdateModes(PetSettings settings)
    {
        settings.UpdateCheckMode = NormalizeUpdateMode(settings.UpdateCheckMode);
        settings.ExtensionUpdateCheckMode = NormalizeUpdateMode(settings.ExtensionUpdateCheckMode);
        settings.ThemeId = string.IsNullOrWhiteSpace(settings.ThemeId) ? ThemeExtensionManager.BundledThemeId : settings.ThemeId.Trim();
        if (settings.ThemeId.Equals(ThemeExtensionManager.RetiredLiquidGlassThemeId, StringComparison.OrdinalIgnoreCase))
            settings.ThemeId = ThemeExtensionManager.BundledThemeId;
        settings.ThemeMode = settings.ThemeMode is "system" or "light" or "dark" ? settings.ThemeMode : "system";
        settings.ThemeBackdrop = settings.ThemeBackdrop is "mica" or "mica-alt" or "acrylic" or "solid" ? settings.ThemeBackdrop : "mica";
    }

    private static string NormalizeUpdateMode(string? mode)
        => mode is "startup" or "daily" or "weekly" or "manual" ? mode : "daily";
}
