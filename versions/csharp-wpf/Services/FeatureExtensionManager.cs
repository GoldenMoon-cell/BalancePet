using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

public sealed class FeatureExtensionManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "feature";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("name_en")] public string NameEn { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("api_version")] public int ApiVersion { get; set; }
    [JsonPropertyName("min_core_version")] public string MinCoreVersion { get; set; } = "0.6.0";
    [JsonPropertyName("update_url")] public string UpdateUrl { get; set; } = "";
    [JsonPropertyName("entrypoint")] public string Entrypoint { get; set; } = "";
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = new();
}

public sealed class FeatureExtensionInfo
{
    public FeatureExtensionManifest Manifest { get; }
    public string DirectoryPath { get; }
    public bool IsEnabled { get; }
    public bool IsRunning { get; internal set; }
    public string DisplayLabel => $"功能扩展 · {Manifest.Name}  v{Manifest.Version}";
    public string Id => Manifest.Id;

    public FeatureExtensionInfo(FeatureExtensionManifest manifest, string directoryPath, bool isEnabled, bool isRunning = false)
    {
        Manifest = manifest;
        DirectoryPath = directoryPath;
        IsEnabled = isEnabled;
        IsRunning = isRunning;
    }
}

/// <summary>
/// Installs and launches feature extensions out of process. The host never
/// loads an extension assembly and never passes provider credentials to it.
/// </summary>
public sealed class FeatureExtensionManager : IDisposable
{
    public const int CurrentApiVersion = 1;
    public const string UsageAnalyticsId = "balancepet.ext.feature.usage-analytics";
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private const long MaxEntryBytes = 100L * 1024 * 1024;
    private const int MaxEntryCount = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".com", ".hta", ".js", ".jse", ".msi", ".ps1", ".scr", ".vbs", ".wsf", ".wsh"
    };
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);

    public string RootDirectory { get; }

    public FeatureExtensionManager(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet", "extensions");
    }

    public IReadOnlyList<FeatureExtensionInfo> GetInstalled()
    {
        if (!Directory.Exists(RootDirectory)) return Array.Empty<FeatureExtensionInfo>();
        var result = new List<FeatureExtensionInfo>();
        foreach (var idDirectory in Directory.EnumerateDirectories(RootDirectory))
        {
            var id = Path.GetFileName(idDirectory);
            if (!IsValidId(id)) continue;
            var enabled = !File.Exists(Path.Combine(idDirectory, ".disabled"));
            foreach (var versionDirectory in Directory.EnumerateDirectories(idDirectory))
            {
                if (Path.GetFileName(versionDirectory).Equals(".staging", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var manifestPath = Path.Combine(versionDirectory, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;
                    var manifest = JsonSerializer.Deserialize<FeatureExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions);
                    if (manifest is null || !IsValidManifest(manifest, out _, checkInstalledConflicts: false)) continue;
                    if (!string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    var running = _processes.TryGetValue(manifest.Id, out var process) && !process.HasExited;
                    result.Add(new FeatureExtensionInfo(manifest, versionDirectory, enabled, running));
                }
                catch (IOException) { }
                catch (JsonException) { }
                catch (InvalidOperationException) { }
            }
        }
        return result
            .OrderBy(info => info.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(info => ParseVersion(info.Manifest.Version))
            .ToArray();
    }

    public FeatureExtensionInfo? GetLatest(string id)
        => GetInstalled().Where(info => string.Equals(info.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(info => ParseVersion(info.Manifest.Version)).FirstOrDefault();

    public FeatureExtensionInfo InstallFeaturePackage(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) throw new FileNotFoundException("功能扩展 ZIP 不存在。", zipPath);
        var packageInfo = new FileInfo(zipPath);
        if (packageInfo.Length <= 0 || packageInfo.Length > MaxPackageBytes) throw new InvalidDataException("功能扩展 ZIP 超过 500 MB 限制。");

        Directory.CreateDirectory(RootDirectory);
        var stagingRoot = Path.Combine(RootDirectory, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntryCount)
                    throw new InvalidDataException("功能扩展文件数量无效，最多允许 4096 个文件。");
                long totalBytes = 0;
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var relative = NormalizeEntryPath(entry.FullName);
                    if (relative.Length == 0) continue;
                    if (!paths.Add(relative)) throw new InvalidDataException($"功能扩展包含重复路径：{relative}。");
                    if (ForbiddenExtensions.Contains(Path.GetExtension(relative)))
                        throw new InvalidDataException($"功能扩展包含不允许的脚本类型：{Path.GetExtension(relative)}。");
                    if (entry.Length > MaxEntryBytes || (totalBytes += entry.Length) > MaxPackageBytes)
                        throw new InvalidDataException("功能扩展解压内容超过大小限制。");
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
            if (!File.Exists(manifestPath)) throw new InvalidDataException("功能扩展根目录缺少 manifest.json。");
            var manifest = JsonSerializer.Deserialize<FeatureExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("功能扩展 manifest.json 为空或格式不正确。");
            if (!IsValidManifest(manifest, out var manifestError)) throw new InvalidDataException(manifestError);
            var entrypoint = GetSafePath(stagingRoot, manifest.Entrypoint);
            if (!File.Exists(entrypoint)) throw new InvalidDataException($"找不到功能扩展入口：{manifest.Entrypoint}。");

            Stop(manifest.Id);
            var idDirectory = Path.Combine(RootDirectory, manifest.Id);
            var destinationRoot = Path.Combine(idDirectory, manifest.Version);
            Directory.CreateDirectory(idDirectory);
            if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, true);
            Directory.Move(stagingRoot, destinationRoot);
            return new FeatureExtensionInfo(manifest, destinationRoot, !File.Exists(Path.Combine(idDirectory, ".disabled")));
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
        if (enabled)
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
        else
        {
            Stop(id);
            File.WriteAllText(marker, "disabled");
        }
        return true;
    }

    public bool Uninstall(string id)
    {
        if (!IsValidId(id)) return false;
        Stop(id);
        var target = Path.GetFullPath(Path.Combine(RootDirectory, id));
        if (!IsWithinRoot(target) || !Directory.Exists(target)) return false;
        Directory.Delete(target, true);
        return true;
    }

    public bool TryLaunch(string id, out string error)
    {
        error = "";
        var extension = GetLatest(id);
        if (extension is null) { error = "功能扩展未安装。"; return false; }
        if (!extension.IsEnabled) { error = "功能扩展已禁用。"; return false; }
        var executablePath = Path.Combine(extension.DirectoryPath, extension.Manifest.Entrypoint);
        if (!File.Exists(executablePath))
        { error = "功能扩展入口文件不存在。"; return false; }

        if (_processes.TryGetValue(id, out var existing) && !existing.HasExited)
        {
            ActivateWhenReady(existing);
            return true;
        }

        // The extension may have survived a host restart. Reuse that process
        // instead of starting a second dashboard that appears to do nothing.
        var existingProcess = FindRunningProcess(executablePath);
        if (existingProcess is not null)
        {
            _processes[id] = existingProcess;
            ActivateWhenReady(existingProcess);
            return true;
        }

        try
        {
            var dataDirectory = UsageEventBridge.GetDefaultDirectory();
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = extension.DirectoryPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                Arguments = $"--data-dir {QuoteArgument(dataDirectory)}"
            };
            var process = Process.Start(startInfo);
            if (process is null) { error = "无法启动功能扩展。"; return false; }
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                if (_processes.TryGetValue(id, out var current) && ReferenceEquals(current, process)) _processes.Remove(id);
                process.Dispose();
            };
            _processes[id] = process;
            ActivateWhenReady(process);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public void Stop(string id)
    {
        if (!_processes.Remove(id, out var process)) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(1500);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }

    public void StopAll()
    {
        foreach (var id in _processes.Keys.ToArray()) Stop(id);
    }

    public void Dispose() => StopAll();

    private bool IsValidManifest(FeatureExtensionManifest manifest, out string error, bool checkInstalledConflicts = true)
    {
        if (!IsValidId(manifest.Id) || !manifest.Id.StartsWith("balancepet.ext.", StringComparison.Ordinal)) { error = "功能扩展 id 必须使用 balancepet.ext. 前缀。"; return false; }
        if (!string.Equals(manifest.Type, "feature", StringComparison.OrdinalIgnoreCase)) { error = "当前只支持 type=feature 的功能扩展。"; return false; }
        if (!IsSemVer(manifest.Version)) { error = "功能扩展 version 必须采用 x.y.z 格式。"; return false; }
        if (manifest.ApiVersion != CurrentApiVersion) { error = $"功能扩展 API 版本不兼容：需要 {CurrentApiVersion}。"; return false; }
        if (!TryVersion(manifest.MinCoreVersion, out var min) || min > CurrentCoreVersion) { error = $"功能扩展需要 BalancePet {manifest.MinCoreVersion} 或更高版本。"; return false; }
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Entrypoint)) { error = "功能扩展 name 和 entrypoint 不能为空。"; return false; }
        if (manifest.Entrypoint.Contains('/') || manifest.Entrypoint.Contains('\\') || Path.IsPathRooted(manifest.Entrypoint) || manifest.Entrypoint.Contains("..", StringComparison.Ordinal)) { error = "功能扩展 entrypoint 必须是根目录下的文件名。"; return false; }
        if ((manifest.Name?.Length ?? 0) > 120 || (manifest.NameEn?.Length ?? 0) > 120) { error = "功能扩展 name/name_en 不能超过 120 个字符。"; return false; }
        if ((manifest.Entrypoint?.Length ?? 0) > 120) { error = "功能扩展 entrypoint 不能超过 120 个字符。"; return false; }
        if (!string.IsNullOrWhiteSpace(manifest.UpdateUrl) && !TryValidateGitHubReleaseUrl(manifest.UpdateUrl)) { error = "功能扩展 update_url 必须是 api.github.com 的 releases/latest 地址。"; return false; }
        if (manifest.Capabilities is null || manifest.Capabilities.Count == 0 || manifest.Capabilities.Distinct(StringComparer.Ordinal).Count() != manifest.Capabilities.Count || manifest.Capabilities.Any(value => value != "usage.read")) { error = "当前只允许声明不重复的 usage.read 能力。"; return false; }
        if (checkInstalledConflicts && GetInstalled().Any(info => string.Equals(info.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase) && !string.Equals(info.Manifest.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))) { /* side-by-side versions are allowed */ }
        error = "";
        return true;
    }

    private static Version CurrentCoreVersion => CoreVersion.Current;
    private static bool IsValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length is >= 8 and <= 96
        && value.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '.' or '-')
        && value.StartsWith("balancepet.ext.", StringComparison.Ordinal)
        && char.IsLetterOrDigit(value[0]) && char.IsLetterOrDigit(value[^1]);
    private static bool IsSemVer(string? value) => !string.IsNullOrWhiteSpace(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?$");
    private static Version ParseVersion(string value) => TryVersion(value, out var version) ? version : new Version(0, 0, 0);
    private static bool TryVersion(string? value, out Version version)
    {
        var numeric = value?.Split('-', '+')[0];
        return Version.TryParse(numeric, out version!);
    }
    private static string NormalizeEntryPath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if ((normalized.Length > 0 && normalized[0] == '/') || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
            throw new InvalidDataException("扩展包含绝对路径。");
        return normalized;
    }

    private static bool TryValidateGitHubReleaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return parts.Length == 5 && parts[0].Equals("repos", StringComparison.OrdinalIgnoreCase) &&
            parts[3].Equals("releases", StringComparison.OrdinalIgnoreCase) && parts[4].Equals("latest", StringComparison.OrdinalIgnoreCase);
    }
    private string GetSafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("扩展包含非法路径。");
        return full;
    }
    private bool IsWithinRoot(string path)
    {
        var root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static Process? FindRunningProcess(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch (InvalidOperationException) { process.Dispose(); }
            catch (System.ComponentModel.Win32Exception) { process.Dispose(); }
        }
        return null;
    }

    private static void ActivateWhenReady(Process process)
    {
        try
        {
            // WPF creates the process before it creates the top-level window.
            // Wait briefly, then keep polling so a launch from the modal
            // settings window still brings the extension to the foreground.
            process.WaitForInputIdle(2000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }

        Activate(process);
        try
        {
            if (process.MainWindowHandle != IntPtr.Zero) return;
            var processId = process.Id;
            _ = Task.Run(() =>
            {
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    Thread.Sleep(100);
                    try
                    {
                        using var candidate = Process.GetProcessById(processId);
                        if (candidate.HasExited) return;
                        candidate.Refresh();
                        if (candidate.MainWindowHandle == IntPtr.Zero) continue;
                        Activate(candidate);
                        return;
                    }
                    catch (ArgumentException) { return; }
                    catch (InvalidOperationException) { return; }
                    catch (System.ComponentModel.Win32Exception) { return; }
                }
            });
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void Activate(Process process)
    {
        try
        {
            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero) return;
            ShowWindow(handle, ShowNormal);
            SetForegroundWindow(handle);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private const int ShowNormal = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
