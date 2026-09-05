using System.IO.Compression;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

public sealed class PetExtensionManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "pet";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("name_en")] public string NameEn { get; set; } = "";
    [JsonPropertyName("style")] public string Style { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("api_version")] public int ApiVersion { get; set; }
    [JsonPropertyName("min_core_version")] public string MinCoreVersion { get; set; } = "0.5.0";
}

public sealed class PetExtensionInfo
{
    public PetExtensionManifest Manifest { get; }
    public string DirectoryPath { get; }
    public bool IsEnabled { get; }
    public string DisplayLabel => $"{Manifest.Name}  v{Manifest.Version}";
    public string StyleId => Manifest.Style;

    public PetExtensionInfo(PetExtensionManifest manifest, string directoryPath, bool isEnabled)
    {
        Manifest = manifest;
        DirectoryPath = directoryPath;
        IsEnabled = isEnabled;
    }
}

/// <summary>
/// Manages resource-only pet extensions. Extension code is intentionally not loaded.
/// </summary>
public sealed class PetExtensionManager
{
    public const int CurrentApiVersion = 1;
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private const long MaxEntryBytes = 100L * 1024 * 1024;
    private const int MaxEntryCount = 2048;
    private static readonly string[] RequiredStates = PetStyleCatalog.RequiredStateFiles.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".msi", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".scr", ".hta", ".zip"
    };

    public string RootDirectory { get; }

    public PetExtensionManager(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet", "extensions");
    }

    public IReadOnlyList<PetExtensionInfo> GetInstalled()
    {
        if (!Directory.Exists(RootDirectory)) return Array.Empty<PetExtensionInfo>();
        var result = new List<PetExtensionInfo>();
        foreach (var idDirectory in Directory.EnumerateDirectories(RootDirectory))
        {
            var id = Path.GetFileName(idDirectory);
            if (!IsValidId(id)) continue;
            var enabled = !File.Exists(Path.Combine(idDirectory, ".disabled"));
            foreach (var versionDirectory in Directory.EnumerateDirectories(idDirectory))
            {
                if (Path.GetFileName(versionDirectory).Equals(".staging", StringComparison.OrdinalIgnoreCase)) continue;
                var manifestPath = Path.Combine(versionDirectory, "manifest.json");
                try
                {
                    if (!File.Exists(manifestPath)) continue;
                    var manifest = JsonSerializer.Deserialize<PetExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions);
                    if (manifest is null || !IsValidManifest(manifest, out _, checkInstalledConflicts: false)) continue;
                    if (!string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(new PetExtensionInfo(manifest, versionDirectory, enabled));
                }
                catch (IOException) { }
                catch (JsonException) { }
            }
        }
        return result
            .OrderBy(info => info.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(info => ParseVersion(info.Manifest.Version))
            .ToArray();
    }

    public IReadOnlyList<PetExtensionInfo> GetLatestEnabled()
        => GetInstalled().Where(info => info.IsEnabled)
            .GroupBy(info => info.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(info => ParseVersion(info.Manifest.Version)).First())
            .ToArray();

    public PetExtensionInfo InstallPetPackage(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) throw new FileNotFoundException("扩展 ZIP 不存在。", zipPath);
        var packageInfo = new FileInfo(zipPath);
        if (packageInfo.Length <= 0 || packageInfo.Length > MaxPackageBytes) throw new InvalidDataException("扩展 ZIP 超过 500 MB 限制。 ");

        Directory.CreateDirectory(RootDirectory);
        var stagingRoot = Path.Combine(RootDirectory, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntryCount)
                    throw new InvalidDataException("扩展文件数量无效，最多允许 2048 个文件。 ");
                long totalBytes = 0;
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var relative = NormalizeEntryPath(entry.FullName);
                    if (relative.Length == 0) continue;
                    if (!paths.Add(relative)) throw new InvalidDataException($"扩展包含重复路径：{relative}。 ");
                    if (ForbiddenExtensions.Contains(Path.GetExtension(relative)))
                        throw new InvalidDataException($"扩展包含不允许的文件类型：{Path.GetExtension(relative)}。 ");
                    if (entry.Length > MaxEntryBytes || (totalBytes += entry.Length) > MaxPackageBytes)
                        throw new InvalidDataException("扩展解压内容超过大小限制。 ");
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

            var manifestPath = Path.Combine(stagingRoot, "manifest.json");
            if (!File.Exists(manifestPath)) throw new InvalidDataException("扩展根目录缺少 manifest.json。 ");
            var manifest = JsonSerializer.Deserialize<PetExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("manifest.json 为空或格式不正确。 ");
            if (!IsValidManifest(manifest, out var manifestError)) throw new InvalidDataException(manifestError);
            var styleDirectory = Path.Combine(stagingRoot, "assets", "pets", manifest.Style);
            foreach (var state in RequiredStates)
                if (!File.Exists(Path.Combine(styleDirectory, state))) throw new InvalidDataException($"缺少状态图：{state}。 ");

            var idDirectory = Path.Combine(RootDirectory, manifest.Id);
            var destinationRoot = Path.Combine(idDirectory, manifest.Version);
            Directory.CreateDirectory(idDirectory);
            if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, true);
            Directory.Move(stagingRoot, destinationRoot);
            return new PetExtensionInfo(manifest, destinationRoot, !File.Exists(Path.Combine(idDirectory, ".disabled")));
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
            TryDeleteDirectory(Path.GetDirectoryName(stagingRoot)!);
        }
    }

    public bool Uninstall(string id)
    {
        if (!IsValidId(id)) return false;
        var target = Path.GetFullPath(Path.Combine(RootDirectory, id));
        if (!IsWithinRoot(target) || !Directory.Exists(target)) return false;
        Directory.Delete(target, true);
        return true;
    }

    public bool SetEnabled(string id, bool enabled)
    {
        if (!IsValidId(id)) return false;
        var target = Path.GetFullPath(Path.Combine(RootDirectory, id));
        if (!IsWithinRoot(target) || !Directory.Exists(target)) return false;
        var marker = Path.Combine(target, ".disabled");
        if (enabled) { if (File.Exists(marker)) File.Delete(marker); }
        else File.WriteAllText(marker, "disabled");
        return true;
    }

    public bool TryGetEnabledStyle(string styleId, out PetExtensionInfo? extension)
    {
        extension = GetLatestEnabled().FirstOrDefault(info => string.Equals(info.StyleId, styleId, StringComparison.OrdinalIgnoreCase));
        return extension is not null;
    }

    private bool IsValidManifest(PetExtensionManifest manifest, out string error, bool checkInstalledConflicts = true)
    {
        if (!IsValidId(manifest.Id)) { error = "扩展 id 必须是 2-64 位小写字母、数字、点或连字符。"; return false; }
        if (!string.Equals(manifest.Type, "pet", StringComparison.OrdinalIgnoreCase)) { error = "当前只支持 type=pet 的资源扩展。"; return false; }
        if (!IsValidId(manifest.Style)) { error = "扩展 style 必须是合法的小写资源 ID。"; return false; }
        if (!IsSemVer(manifest.Version)) { error = "扩展 version 必须采用 x.y.z 格式。"; return false; }
        if (manifest.ApiVersion != CurrentApiVersion) { error = $"扩展 API 版本不兼容：需要 {CurrentApiVersion}。"; return false; }
        var min = new Version(0, 0, 0);
        if (!string.IsNullOrWhiteSpace(manifest.MinCoreVersion) && !TryVersion(manifest.MinCoreVersion, out min))
        { error = "扩展 min_core_version 必须采用 x.y.z 格式。"; return false; }
        if (min > CurrentCoreVersion)
        { error = $"扩展需要 BalancePet {manifest.MinCoreVersion} 或更高版本。"; return false; }
        if (string.IsNullOrWhiteSpace(manifest.Name)) { error = "扩展 name 不能为空。"; return false; }
        if (PetStyleCatalog.All.Any(style => string.Equals(style.Id, manifest.Style, StringComparison.OrdinalIgnoreCase)))
        { error = $"style 已与内置形象冲突：{manifest.Style}。"; return false; }
        if (checkInstalledConflicts && GetInstalled().Any(info => string.Equals(info.StyleId, manifest.Style, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(info.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase)))
        { error = $"style 已被其他扩展占用：{manifest.Style}。"; return false; }
        error = "";
        return true;
    }

    private static Version CurrentCoreVersion => new(0, 5, 0);
    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 2 and <= 64
        && value.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '.' or '-')
        && IsAsciiAlphaNumeric(value[0]) && IsAsciiAlphaNumeric(value[^1]);
    private static bool IsAsciiAlphaNumeric(char ch)
        => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9');
    private static bool IsSemVer(string? value) => !string.IsNullOrWhiteSpace(value)
        && System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?$");
    private static Version ParseVersion(string value) => TryVersion(value, out var version) ? version : new Version(0, 0, 0);
    private static bool TryVersion(string? value, out Version version)
    {
        var numeric = value?.Split('-', '+')[0];
        return Version.TryParse(numeric, out version!);
    }
    private static string NormalizeEntryPath(string value) => value.Replace('\\', '/').TrimStart('/');
    private string GetSafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("扩展包含非法路径。 ");
        return full;
    }
    private bool IsWithinRoot(string path)
    {
        var root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
