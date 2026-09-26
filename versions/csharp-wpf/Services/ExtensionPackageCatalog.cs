using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;

namespace BalancePet.Wpf.Services;

public sealed class ExtensionPackageDescriptor
{
    public string PackagePath { get; }
    public string PackageFileName => Path.GetFileName(PackagePath);
    public string Id { get; }
    public string Type { get; }
    public string Name { get; }
    public string NameEn { get; }
    public string Version { get; }
    public string MinCoreVersion { get; }
    public string UpdateUrl { get; }
    public string DisplayLabel => $"{Type switch { "feature" => "功能扩展", "theme" => "主题扩展", _ => "资源扩展" }} · {Name}  v{Version}";

    public ExtensionPackageDescriptor(string packagePath, string id, string type, string name, string nameEn, string version, string minCoreVersion, string updateUrl = "")
    {
        PackagePath = packagePath;
        Id = id;
        Type = type;
        Name = name;
        NameEn = nameEn;
        Version = version;
        MinCoreVersion = minCoreVersion;
        UpdateUrl = updateUrl;
    }
}

public sealed class ExtensionCatalogEntry
{
    private readonly IReadOnlyList<ExtensionPackageDescriptor> _packages;
    private readonly IReadOnlyList<PetExtensionInfo> _pets;
    private readonly IReadOnlyList<FeatureExtensionInfo> _features;
    private readonly IReadOnlyList<ThemeExtensionInfo> _themes;
    public ExtensionUpdateRelease? RemoteUpdate { get; set; }
    public ExtensionPackageDescriptor? Package => _packages.FirstOrDefault();
    public PetExtensionInfo? Pet => _pets.FirstOrDefault();
    public FeatureExtensionInfo? Feature => _features.FirstOrDefault();
    public ThemeExtensionInfo? Theme => _themes.FirstOrDefault();
    public bool IsEnglish { get; set; }
    public string Id => Package?.Id ?? Pet?.Manifest.Id ?? Feature?.Manifest.Id ?? Theme?.Manifest.Id ?? "";
    public string Type => Package?.Type ?? (Pet is not null ? "pet" : Theme is not null ? "theme" : "feature");
    public bool IsInstalled => Pet is not null || Feature is not null || Theme is not null;
    public bool IsEnabled => Pet?.IsEnabled == true || Feature?.IsEnabled == true || Theme?.IsEnabled == true;
    public bool IsRunning => Feature?.IsRunning == true;
    public bool IsBundledTheme => Theme is not null && string.Equals(Theme.Manifest.Id, ThemeExtensionManager.BundledThemeId, StringComparison.OrdinalIgnoreCase);
    public bool HasPackage => Package is not null;
    public string InstalledVersion => Pet?.Manifest.Version ?? Feature?.Manifest.Version ?? Theme?.Manifest.Version ?? "";
    public string UpdateUrl => _packages.Select(value => value.UpdateUrl)
        .Concat(Pet is null ? Array.Empty<string>() : new[] { Pet.Manifest.UpdateUrl })
        .Concat(Feature is null ? Array.Empty<string>() : new[] { Feature.Manifest.UpdateUrl })
        .Concat(Theme is null ? Array.Empty<string>() : new[] { Theme.Manifest.UpdateUrl })
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    public string AvailableVersion
    {
        get
        {
            var local = Package?.Version ?? "";
            var remote = RemoteUpdate?.Version ?? "";
            return CompareVersions(remote, local) > 0 ? remote : local;
        }
    }
    public bool HasUpdate => IsInstalled && CompareVersions(AvailableVersion, InstalledVersion) > 0;
    public bool HasRemoteUpdate => IsInstalled && RemoteUpdate is not null && CompareVersions(RemoteUpdate.Version, InstalledVersion) > 0;
    public bool CanInstallOrUninstall => IsInstalled ? !IsBundledTheme : Package is not null;
    public bool CanLaunch => Feature?.IsEnabled == true;
    public Visibility ToggleVisibility => Type == "theme" ? Visibility.Collapsed : Visibility.Visible;
    // Feature extensions are enabled and started as part of installation;
    // opening their panel belongs to the app's own entry points, not a row
    // level "Launch" action.
    public Visibility LaunchVisibility => Visibility.Collapsed;
    public string InstallGlyph => IsInstalled ? "×" : "⇩";
    public string ToggleGlyph => !IsInstalled ? "—" : IsEnabled ? "◉" : "○";
    public string UpdateGlyph => "↻";
    public string InstallActionText => IsBundledTheme
        ? (IsEnglish ? "Built in" : "内置")
        : IsEnglish ? (IsInstalled ? "Uninstall" : "Install") : (IsInstalled ? "卸载" : "安装");
    public string ToggleActionText => !IsInstalled ? (IsEnglish ? "Enable" : "启用") : IsEnabled ? (IsEnglish ? "Disable" : "禁用") : (IsEnglish ? "Enable" : "启用");
    public string UpdateActionText => IsEnglish ? "Update" : "更新";
    public string LaunchActionText => IsEnglish ? "Launch" : "启动";
    public string InstallTooltip => IsBundledTheme
        ? (IsEnglish ? "Bundled fallback theme" : "内置回退主题")
        : IsEnglish ? (IsInstalled ? "Uninstall" : "Install") : (IsInstalled ? "卸载" : "安装");
    public string ToggleTooltip => IsEnglish ? (IsEnabled ? "Disable" : "Enable") : (IsEnabled ? "禁用" : "启用");
    public string UpdateTooltip => IsEnglish ? $"Update to v{AvailableVersion}" : $"更新到 v{AvailableVersion}";
    public string LaunchTooltip => IsEnglish ? "Started automatically" : "安装后自动运行";
    public string UpdateStatusText => HasUpdate
        ? IsEnglish ? $"Latest version: v{AvailableVersion}" : $"发现新版本：v{AvailableVersion}"
        : IsInstalled
            ? IsEnglish ? "Up to date" : "已是最新版本"
            : IsEnglish ? $"Available: v{AvailableVersion}" : $"可安装：v{AvailableVersion}";
    public string DisplayLabel
    {
        get
        {
            var name = Package?.Name ?? Pet?.Manifest.Name ?? Feature?.Manifest.Name ?? Theme?.Manifest.Name ?? Id;
            var kind = Type switch { "feature" => "功能扩展", "theme" => "主题扩展", _ => "资源扩展" };
            var version = IsInstalled ? $"已安装 v{InstalledVersion}" : "未安装";
            var latest = HasUpdate ? $" · 最新 v{AvailableVersion}" : !IsInstalled && Package is not null ? $" · 可安装 v{AvailableVersion}" : "";
            var status = !IsInstalled ? "未安装" : Type == "theme" ? "可在外观中选择" : IsRunning ? "运行中" : IsEnabled ? "已启用" : "已禁用";
            return $"{kind} · {name}  {version}{latest}  [{status}]";
        }
    }

    public string DisplayText => IsEnglish ? DisplayLabelEn : DisplayLabel;

    public string DisplayLabelEn
    {
        get
        {
            var name = Package?.NameEn ?? Pet?.Manifest.NameEn ?? Feature?.Manifest.NameEn ?? Theme?.Manifest.NameEn;
            if (string.IsNullOrWhiteSpace(name)) name = Package?.Name ?? Pet?.Manifest.Name ?? Feature?.Manifest.Name ?? Theme?.Manifest.Name ?? Id;
            var kind = Type switch { "feature" => "Feature extension", "theme" => "Theme extension", _ => "Resource extension" };
            var version = IsInstalled ? $"Installed v{InstalledVersion}" : "Not installed";
            var latest = HasUpdate ? $" · Latest v{AvailableVersion}" : !IsInstalled && Package is not null ? $" · Available v{AvailableVersion}" : "";
            var status = !IsInstalled ? "Not installed" : Type == "theme" ? "Available in Appearance" : IsRunning ? "Running" : IsEnabled ? "Enabled" : "Disabled";
            return $"{kind} · {name}  {version}{latest}  [{status}]";
        }
    }

    public ExtensionCatalogEntry(IEnumerable<ExtensionPackageDescriptor> packages, IEnumerable<PetExtensionInfo> pets, IEnumerable<FeatureExtensionInfo> features, IEnumerable<ThemeExtensionInfo>? themes = null)
    {
        _packages = packages.OrderByDescending(value => ParseVersion(value.Version)).ToArray();
        _pets = pets.OrderByDescending(value => ParseVersion(value.Manifest.Version)).ToArray();
        _features = features.OrderByDescending(value => ParseVersion(value.Manifest.Version)).ToArray();
        _themes = (themes ?? Array.Empty<ThemeExtensionInfo>()).OrderByDescending(value => ParseVersion(value.Manifest.Version)).ToArray();
    }

    public static int CompareVersions(string left, string right) => ParseVersion(left).CompareTo(ParseVersion(right));

    private static Version ParseVersion(string value)
    {
        var numeric = value?.Split('-', '+')[0];
        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0, 0);
    }
}

/// <summary>
/// Scans the user-managed extension library. ZIPs remain untouched until the
/// user explicitly installs one, and uninstalling never removes the ZIP.
/// </summary>
public sealed class ExtensionPackageCatalog
{
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public string RootDirectory { get; }

    public ExtensionPackageCatalog(string? rootDirectory = null)
    {
        // Keep the package library beside the executable so portable installs
        // and normal user installs have one obvious, inspectable location.
        // Installed runtime copies remain under LocalAppData and are managed
        // by the extension managers, so an application upgrade cannot delete
        // them accidentally.
        RootDirectory = rootDirectory ?? Path.Combine(AppContext.BaseDirectory, "extension-library");
    }

    public IReadOnlyList<ExtensionPackageDescriptor> Scan()
    {
        return ScanAll()
            .GroupBy(value => $"{value.Id}@{value.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(value => value.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(value => value.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int CleanupOldPackages()
    {
        var all = ScanAll();
        var removed = 0;
        foreach (var group in all.GroupBy(value => $"{value.Type}:{value.Id}", StringComparer.OrdinalIgnoreCase))
        {
            var keepVersion = group.Max(value => ParseVersion(value.Version));
            var candidates = group
                .Where(value => ParseVersion(value.Version).CompareTo(keepVersion) == 0)
                .OrderByDescending(value => IsCanonicalPackageName(value.PackageFileName, value.Version))
                .ThenByDescending(value => File.GetLastWriteTimeUtc(value.PackagePath))
                .ToArray();
            var keep = candidates.FirstOrDefault();
            foreach (var package in group)
            {
                if (ReferenceEquals(package, keep)) continue;
                try
                {
                    File.Delete(package.PackagePath);
                    removed++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return removed;
    }

    private IReadOnlyList<ExtensionPackageDescriptor> ScanAll()
    {
        try
        {
            EnsureDirectory();
        }
        catch (IOException) { return Array.Empty<ExtensionPackageDescriptor>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<ExtensionPackageDescriptor>(); }
        var result = new List<ExtensionPackageDescriptor>();
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(RootDirectory, "*.zip", SearchOption.TopDirectoryOnly); }
        catch (IOException) { return Array.Empty<ExtensionPackageDescriptor>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<ExtensionPackageDescriptor>(); }
        foreach (var path in files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxPackageBytes) continue;
                if (TryReadDescriptor(path, out var descriptor) && descriptor is not null &&
                    !descriptor.Id.Equals(ThemeExtensionManager.RetiredLiquidGlassThemeId, StringComparison.OrdinalIgnoreCase))
                    result.Add(descriptor);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }
            catch (JsonException) { }
            catch (NotSupportedException) { }
        }
        return result;
    }

    public IReadOnlyList<ExtensionCatalogEntry> BuildEntries(
        IEnumerable<PetExtensionInfo> pets,
        IEnumerable<FeatureExtensionInfo> features,
        IEnumerable<ThemeExtensionInfo>? themes = null)
    {
        var petValues = pets.ToArray();
        var featureValues = features.ToArray();
        var themeValues = (themes ?? Array.Empty<ThemeExtensionInfo>()).ToArray();
        var packages = Scan();
        return packages
            .Concat(petValues.Select(info => new ExtensionPackageDescriptor("", info.Manifest.Id, "pet", info.Manifest.Name, info.Manifest.NameEn, info.Manifest.Version, info.Manifest.MinCoreVersion, info.Manifest.UpdateUrl)))
            .Concat(featureValues.Select(info => new ExtensionPackageDescriptor("", info.Manifest.Id, "feature", info.Manifest.Name, info.Manifest.NameEn, info.Manifest.Version, info.Manifest.MinCoreVersion, info.Manifest.UpdateUrl)))
            .Concat(themeValues.Select(info => new ExtensionPackageDescriptor("", info.Manifest.Id, "theme", info.Manifest.Name, info.Manifest.NameEn, info.Manifest.Version, info.Manifest.MinCoreVersion, info.Manifest.UpdateUrl)))
            .GroupBy(value => $"{value.Type}:{value.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => new ExtensionCatalogEntry(
                group.Where(value => !string.IsNullOrWhiteSpace(value.PackagePath)),
                petValues.Where(info => group.First().Type == "pet" && string.Equals(info.Manifest.Id, group.First().Id, StringComparison.OrdinalIgnoreCase)),
                featureValues.Where(info => group.First().Type == "feature" && string.Equals(info.Manifest.Id, group.First().Id, StringComparison.OrdinalIgnoreCase)),
                themeValues.Where(info => group.First().Type == "theme" && string.Equals(info.Manifest.Id, group.First().Id, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(entry => entry.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void EnsureDirectory() => Directory.CreateDirectory(RootDirectory);

    public string ImportPackage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("扩展 ZIP 不存在。", sourcePath);
        var source = Path.GetFullPath(sourcePath);
        var root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        EnsureDirectory();
        if (source.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return source;
        var fileInfo = new FileInfo(source);
        if (fileInfo.Length <= 0 || fileInfo.Length > MaxPackageBytes) throw new InvalidDataException("扩展 ZIP 超过 500 MB 限制。");
        if (!TryReadDescriptor(source, out var descriptor) || descriptor is null) throw new InvalidDataException("无法读取扩展 manifest.json。");
        var safeName = new string(Path.GetFileName(source).Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = $"{descriptor.Id}-{descriptor.Version}.zip";
        var destination = Path.Combine(RootDirectory, safeName);
        if (File.Exists(destination) && !string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            // Re-importing the same extension/version should replace the
            // canonical package instead of creating an endless GUID-suffixed
            // duplicate in the library.
            if (!TryReadDescriptor(destination, out var existing)
                || existing is null
                || !string.Equals(existing.Id, descriptor.Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existing.Type, descriptor.Type, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existing.Version, descriptor.Version, StringComparison.OrdinalIgnoreCase))
                destination = Path.Combine(RootDirectory, $"{descriptor.Id}-{descriptor.Version}-{Guid.NewGuid():N}.zip");
        }
        File.Copy(source, destination, true);
        return destination;
    }

    private static bool IsCanonicalPackageName(string fileName, string version)
        => fileName.Contains($"-{version}-win-x64.zip", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith($"-{version}.zip", StringComparison.OrdinalIgnoreCase);

    private static Version ParseVersion(string value)
    {
        var numeric = value?.Split('-', '+')[0];
        return Version.TryParse(numeric, out var version) ? version : new Version(0, 0, 0);
    }

    public void OpenFolder()
    {
        EnsureDirectory();
        Process.Start(new ProcessStartInfo("explorer.exe", RootDirectory) { UseShellExecute = true });
    }

    private static bool TryReadDescriptor(string path, out ExtensionPackageDescriptor? descriptor)
    {
        descriptor = null;
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count == 0 || archive.Entries.Count > 4096) return false;
        var manifestEntry = archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null || manifestEntry.Length > 256 * 1024) return false;
        using var reader = new StreamReader(manifestEntry.Open());
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        var root = document.RootElement;
        var id = ReadString(root, "id");
        var type = ReadString(root, "type").ToLowerInvariant();
        var name = ReadString(root, "name");
        var nameEn = ReadString(root, "name_en");
        var version = ReadString(root, "version");
        var minCore = ReadString(root, "min_core_version");
        var updateUrl = ReadString(root, "update_url");
        if (string.IsNullOrWhiteSpace(id) || type is not ("pet" or "feature" or "theme") || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) return false;
        descriptor = new ExtensionPackageDescriptor(path, id, type, name, nameEn, version, minCore, updateUrl);
        return true;
    }

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
}
