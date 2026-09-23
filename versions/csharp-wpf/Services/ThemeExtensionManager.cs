using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BalancePet.Wpf.Services;

public sealed class ThemeExtensionManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "theme";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("name_en")] public string NameEn { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("api_version")] public int ApiVersion { get; set; }
    [JsonPropertyName("min_core_version")] public string MinCoreVersion { get; set; } = "1.0.0";
    [JsonPropertyName("update_url")] public string UpdateUrl { get; set; } = "";
    [JsonPropertyName("theme_file")] public string ThemeFile { get; set; } = "theme.json";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("description_en")] public string DescriptionEn { get; set; } = "";
}

public sealed class ThemePalette
{
    [JsonPropertyName("window")] public string Window { get; set; } = "";
    [JsonPropertyName("sidebar")] public string Sidebar { get; set; } = "";
    [JsonPropertyName("surface")] public string Surface { get; set; } = "";
    [JsonPropertyName("surface_strong")] public string SurfaceStrong { get; set; } = "";
    [JsonPropertyName("control")] public string Control { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("muted")] public string Muted { get; set; } = "";
    [JsonPropertyName("border")] public string Border { get; set; } = "";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "";
    [JsonPropertyName("accent_soft")] public string AccentSoft { get; set; } = "";
    [JsonPropertyName("danger")] public string Danger { get; set; } = "";
    [JsonPropertyName("warning_surface")] public string WarningSurface { get; set; } = "";
    [JsonPropertyName("warning_text")] public string WarningText { get; set; } = "";
}

public sealed class ThemeDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("preferred_backdrop")] public string PreferredBackdrop { get; set; } = "mica";
    [JsonPropertyName("corner_radius")] public double CornerRadius { get; set; } = 8;
    [JsonPropertyName("light")] public ThemePalette Light { get; set; } = new();
    [JsonPropertyName("dark")] public ThemePalette Dark { get; set; } = new();
}

public sealed class ThemeExtensionInfo
{
    public ThemeExtensionManifest Manifest { get; }
    public ThemeDocument Theme { get; }
    public string DirectoryPath { get; }
    public bool IsEnabled { get; }

    public ThemeExtensionInfo(ThemeExtensionManifest manifest, ThemeDocument theme, string directoryPath, bool isEnabled)
    {
        Manifest = manifest;
        Theme = theme;
        DirectoryPath = directoryPath;
        IsEnabled = isEnabled;
    }
}

/// <summary>
/// Manages declarative theme extensions. Theme packages never load XAML,
/// assemblies, scripts, fonts, or any other executable content.
/// </summary>
public sealed class ThemeExtensionManager
{
    public const int CurrentApiVersion = 1;
    public const string BundledThemeId = "balancepet.theme.mica";
    public const string RetiredLiquidGlassThemeId = "balancepet.theme.liquid-glass";
    private const long MaxPackageBytes = 4L * 1024 * 1024;
    private const long MaxEntryBytes = 2L * 1024 * 1024;
    private const int MaxEntryCount = 16;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".json", ".png" };

    public string RootDirectory { get; }

    public ThemeExtensionManager(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet", "extensions");
    }

    public void EnsureBundledThemeInstalled()
    {
        RemoveRetiredThemes();
        var source = Path.Combine(AppContext.BaseDirectory, "assets", "themes", "balancepet-mica");
        var manifestPath = Path.Combine(source, "manifest.json");
        var themePath = Path.Combine(source, "theme.json");
        if (!File.Exists(manifestPath) || !File.Exists(themePath)) return;

        var manifest = ReadManifest(manifestPath);
        var theme = ReadTheme(themePath);
        if (!IsValidManifest(manifest, out _) || !IsValidTheme(theme, out _)) return;

        var destination = Path.Combine(RootDirectory, manifest.Id, manifest.Version);
        var idDirectory = Path.GetDirectoryName(destination)!;
        var disabledMarker = Path.Combine(idDirectory, ".disabled");
        if (File.Exists(disabledMarker)) File.Delete(disabledMarker);
        if (File.Exists(Path.Combine(destination, "manifest.json")) && File.Exists(Path.Combine(destination, "theme.json"))) return;
        Directory.CreateDirectory(destination);
        File.Copy(manifestPath, Path.Combine(destination, "manifest.json"), true);
        File.Copy(themePath, Path.Combine(destination, "theme.json"), true);
    }

    private void RemoveRetiredThemes()
    {
        TryDeleteDirectory(Path.Combine(RootDirectory, RetiredLiquidGlassThemeId));
    }

    public IReadOnlyList<ThemeExtensionInfo> GetInstalled()
    {
        if (!Directory.Exists(RootDirectory)) return Array.Empty<ThemeExtensionInfo>();
        var result = new List<ThemeExtensionInfo>();
        foreach (var idDirectory in Directory.EnumerateDirectories(RootDirectory))
        {
            var id = Path.GetFileName(idDirectory);
            if (!IsValidId(id)) continue;
            if (id.Equals(RetiredLiquidGlassThemeId, StringComparison.OrdinalIgnoreCase)) continue;
            // Themes are available choices, not independently enabled modules.
            // Ignore legacy disabled markers left by early preview builds.
            const bool enabled = true;
            foreach (var versionDirectory in Directory.EnumerateDirectories(idDirectory))
            {
                if (Path.GetFileName(versionDirectory).Equals(".staging", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var manifest = ReadManifest(Path.Combine(versionDirectory, "manifest.json"));
                    if (!IsValidManifest(manifest, out _) || !string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    var themePath = GetSafeThemePath(versionDirectory, manifest.ThemeFile);
                    var theme = ReadTheme(themePath);
                    if (!IsValidTheme(theme, out _)) continue;
                    result.Add(new ThemeExtensionInfo(manifest, theme, versionDirectory, enabled));
                }
                catch (Exception error) when (error is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { }
            }
        }

        return result.OrderBy(info => info.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(info => ParseVersion(info.Manifest.Version)).ToArray();
    }

    public IReadOnlyList<ThemeExtensionInfo> GetLatestEnabled()
        => GetInstalled().Where(info => info.IsEnabled)
            .GroupBy(info => info.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(info => ParseVersion(info.Manifest.Version)).First()).ToArray();

    public ThemeExtensionInfo? GetLatestEnabled(string id)
        => GetLatestEnabled().FirstOrDefault(info => string.Equals(info.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

    public ThemeExtensionInfo InstallThemePackage(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) throw new FileNotFoundException("主题扩展 ZIP 不存在。", zipPath);
        var packageInfo = new FileInfo(zipPath);
        if (packageInfo.Length <= 0 || packageInfo.Length > MaxPackageBytes) throw new InvalidDataException("主题扩展 ZIP 超过 4 MB 限制。");

        Directory.CreateDirectory(RootDirectory);
        var stagingRoot = Path.Combine(RootDirectory, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntryCount)
                    throw new InvalidDataException("主题扩展文件数量无效，最多允许 16 个文件。");
                long totalBytes = 0;
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var relative = NormalizeEntryPath(entry.FullName);
                    if (relative.Length == 0) continue;
                    if (!paths.Add(relative)) throw new InvalidDataException($"主题扩展包含重复路径：{relative}。");
                    if (!AllowedExtensions.Contains(Path.GetExtension(relative)))
                        throw new InvalidDataException($"主题扩展包含不允许的文件类型：{Path.GetExtension(relative)}。");
                    if (entry.Length > MaxEntryBytes || (totalBytes += entry.Length) > MaxPackageBytes)
                        throw new InvalidDataException("主题扩展解压内容超过大小限制。");
                    var destination = GetSafePath(stagingRoot, relative);
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var source = entry.Open();
                    using var target = File.Create(destination);
                    source.CopyTo(target);
                }
            }

            var manifest = ReadManifest(Path.Combine(stagingRoot, "manifest.json"));
            if (!IsValidManifest(manifest, out var manifestError)) throw new InvalidDataException(manifestError);
            var theme = ReadTheme(GetSafeThemePath(stagingRoot, manifest.ThemeFile));
            if (!IsValidTheme(theme, out var themeError)) throw new InvalidDataException(themeError);

            var idDirectory = Path.Combine(RootDirectory, manifest.Id);
            var destinationRoot = Path.Combine(idDirectory, manifest.Version);
            Directory.CreateDirectory(idDirectory);
            var disabledMarker = Path.Combine(idDirectory, ".disabled");
            if (File.Exists(disabledMarker)) File.Delete(disabledMarker);
            if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, true);
            Directory.Move(stagingRoot, destinationRoot);
            return new ThemeExtensionInfo(manifest, theme, destinationRoot, true);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteDirectory(Path.GetDirectoryName(stagingRoot)!);
        }
    }

    public bool SetEnabled(string id, bool enabled)
    {
        if (!IsValidId(id)) return false;
        var target = Path.GetFullPath(Path.Combine(RootDirectory, id));
        if (!IsWithinRoot(target) || !Directory.Exists(target)) return false;
        var marker = Path.Combine(target, ".disabled");
        if (File.Exists(marker)) File.Delete(marker);
        return enabled;
    }

    public bool Uninstall(string id)
    {
        if (!IsValidId(id)) return false;
        var target = Path.GetFullPath(Path.Combine(RootDirectory, id));
        if (!IsWithinRoot(target) || !Directory.Exists(target)) return false;
        Directory.Delete(target, true);
        return true;
    }

    private static ThemeExtensionManifest ReadManifest(string path)
        => JsonSerializer.Deserialize<ThemeExtensionManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("manifest.json 为空或格式不正确。");

    private static ThemeDocument ReadTheme(string path)
        => JsonSerializer.Deserialize<ThemeDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("theme.json 为空或格式不正确。");

    private static bool IsValidManifest(ThemeExtensionManifest manifest, out string error)
    {
        if (!IsValidId(manifest.Id)) { error = "主题扩展 id 必须是 2-64 位小写字母、数字、点或连字符。"; return false; }
        if (!string.Equals(manifest.Type, "theme", StringComparison.OrdinalIgnoreCase)) { error = "当前只支持 type=theme 的主题扩展。"; return false; }
        if (string.IsNullOrWhiteSpace(manifest.Name)) { error = "主题扩展 name 不能为空。"; return false; }
        if (!IsSemVer(manifest.Version)) { error = "主题扩展 version 必须采用 x.y.z 格式。"; return false; }
        if (manifest.ApiVersion != CurrentApiVersion) { error = $"主题扩展 API 版本不兼容：需要 {CurrentApiVersion}。"; return false; }
        if (!TryVersion(manifest.MinCoreVersion, out var minCore)) { error = "主题扩展 min_core_version 必须采用 x.y.z 格式。"; return false; }
        if (minCore > CoreVersion.Current) { error = $"主题扩展需要 BalancePet {manifest.MinCoreVersion} 或更高版本。"; return false; }
        if (!string.Equals(NormalizeEntryPath(manifest.ThemeFile), "theme.json", StringComparison.OrdinalIgnoreCase))
        { error = "主题扩展 theme_file 必须指向根目录 theme.json。"; return false; }
        error = "";
        return true;
    }

    private static bool IsValidTheme(ThemeDocument theme, out string error)
    {
        if (theme.SchemaVersion != 1) { error = "主题令牌 schema_version 必须为 1。"; return false; }
        if (theme.PreferredBackdrop is not ("mica" or "mica-alt" or "acrylic" or "solid")) { error = "preferred_backdrop 只能是 mica、mica-alt、acrylic 或 solid。"; return false; }
        if (theme.CornerRadius is < 0 or > 16) { error = "corner_radius 必须在 0 到 16 之间。"; return false; }
        if (!IsValidPalette(theme.Light) || !IsValidPalette(theme.Dark)) { error = "主题颜色必须使用 #RRGGBB 或 #AARRGGBB 格式。"; return false; }
        error = "";
        return true;
    }

    private static bool IsValidPalette(ThemePalette value)
        => new[] { value.Window, value.Sidebar, value.Surface, value.SurfaceStrong, value.Control, value.Text, value.Muted, value.Border, value.Accent, value.AccentSoft, value.Danger, value.WarningSurface, value.WarningText }
            .All(IsColor);

    private static bool IsColor(string? value)
        => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");

    private static string GetSafeThemePath(string root, string relative)
        => GetSafePath(root, NormalizeEntryPath(relative));

    private static string NormalizeEntryPath(string value) => value.Replace('\\', '/').TrimStart('/');
    private static string GetSafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("主题扩展包含非法路径。");
        return full;
    }

    private bool IsWithinRoot(string path)
    {
        var root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 2 and <= 64
        && value.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '.' or '-')
        && char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]);
    private static bool IsSemVer(string? value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?$");
    private static Version ParseVersion(string value) => TryVersion(value, out var version) ? version : new Version(0, 0, 0);
    private static bool TryVersion(string? value, out Version version) => Version.TryParse(value?.Split('-', '+')[0], out version!);
    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
