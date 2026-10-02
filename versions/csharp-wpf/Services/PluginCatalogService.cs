using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

public sealed class PluginCatalogDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("plugins")] public List<PluginCatalogRecord> Plugins { get; set; } = new();
}

/// <summary>
/// The appearance catalog, which is a different document rather than a variant of
/// <see cref="PluginCatalogDocument"/>.
/// </summary>
/// <remarks>
/// It names itself and lists entries under <c>appearances</c>, so the two catalogs
/// cannot be mistaken for one another. That is the whole reason the shape differs
/// instead of reusing the plugin document: they are published from separate
/// repositories, on separate schedules, and a reader has to be able to tell which
/// one it is holding.
/// </remarks>
public sealed class AppearanceCatalogDocument
{
    [JsonPropertyName("catalog")] public string Catalog { get; set; } = "";
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("appearances")] public List<PluginCatalogRecord> Appearances { get; set; } = new();
}

public sealed class PluginCatalogRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "feature";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("name_en")] public string NameEn { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("description_en")] public string DescriptionEn { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("min_core_version")] public string MinCoreVersion { get; set; } = "";
    [JsonPropertyName("update_url")] public string UpdateUrl { get; set; } = "";
    [JsonPropertyName("download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("repository_url")] public string RepositoryUrl { get; set; } = "";
    [JsonPropertyName("release_url")] public string ReleaseUrl { get; set; } = "";
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
}

public sealed record PluginCatalogLoadResult(
    IReadOnlyList<PluginCatalogRecord> Entries,
    bool FromRemote,
    bool FromCache,
    string? Error);

/// <summary>
/// Loads the curated, static plugin directory. The catalog is discovery-only:
/// package installation still goes through ExtensionPackageCatalog and the
/// normal ZIP/manifest validation path.
/// </summary>
public sealed class PluginCatalogService
{
    public const int CurrentSchemaVersion = 1;
    public const string CatalogUrl = "https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet/main/plugin-catalog.json";

    /// <summary>
    /// The appearance catalog, published from its own repository.
    /// </summary>
    /// <remarks>
    /// A second source rather than entries appended to the plugin catalog, because
    /// appearances are published on their own schedule from a repository that
    /// describes nothing else, and one catalog being unreachable must not empty the
    /// other. Reading two documents costs one extra request and keeps a new
    /// appearance from needing a commit in the main repository.
    /// </remarks>
    public const string AppearanceCatalogUrl = "https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet-Pets/main/catalog.json";

    /// <summary>The identity an appearance catalog declares, since its URL could be wrong.</summary>
    public const string AppearanceCatalogIdentity = "balancepet.appearances";

    private const long MaxCatalogBytes = 2L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly HttpClient _http;

    public static string CachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "plugin-catalog.json");

    /// <summary>Cached separately, so refreshing one catalog cannot discard the other.</summary>
    public static string AppearanceCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BalancePet", "appearance-catalog.json");

    public static string LocalCatalogPath => Path.Combine(AppContext.BaseDirectory, "plugin-catalog.json");

    /// <summary>
    /// One catalog: where to fetch it, where to cache it, and how to read it.
    /// </summary>
    /// <remarks>
    /// The appearance catalog has no shipped copy. The application carries one
    /// appearance of its own as a fallback, so an installation that has never reached
    /// the network still has a pet; listing packages it cannot download would only
    /// offer the user something that cannot be installed.
    /// </remarks>
    private sealed record CatalogSource(
        string Url,
        string CachePath,
        string? LocalPath,
        string What,
        Func<string, IReadOnlyList<PluginCatalogRecord>> Parse);

    private static readonly CatalogSource[] Sources =
    {
        new(CatalogUrl, CachePath, LocalCatalogPath, "插件目录", Parse),
        new(AppearanceCatalogUrl, AppearanceCachePath, null, "形象目录", ParseAppearances)
    };

    public PluginCatalogService(HttpClient http) => _http = http;

    public async Task<PluginCatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<PluginCatalogRecord>();
        var errors = new List<string>();
        var fromRemote = false;
        var fromCache = false;

        foreach (var source in Sources)
        {
            var loaded = await LoadSourceAsync(source, cancellationToken);
            entries.AddRange(loaded.Entries);
            if (!string.IsNullOrWhiteSpace(loaded.Error)) errors.Add(loaded.Error);
            fromRemote |= loaded.FromRemote;
            fromCache |= loaded.FromCache;
        }

        // An id published by both catalogs is a mistake rather than something to
        // resolve here: the list can only install one of them, and silently keeping
        // the first would hide which.
        var unique = new List<PluginCatalogRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) if (seen.Add(entry.Id)) unique.Add(entry);

        return new PluginCatalogLoadResult(
            unique.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            fromRemote,
            fromCache,
            errors.Count == 0 ? null : string.Join("；", errors));
    }

    private async Task<PluginCatalogLoadResult> LoadSourceAsync(CatalogSource source, CancellationToken cancellationToken)
    {
        string? remoteError = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("BalancePet-Plugin-Catalog/1.0");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxCatalogBytes)
                throw new InvalidDataException($"{source.What}文件过大。");
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (json.Length > MaxCatalogBytes) throw new InvalidDataException($"{source.What}文件过大。");
            var entries = source.Parse(json);
            SaveCache(source.CachePath, json);
            return new PluginCatalogLoadResult(entries, FromRemote: true, FromCache: false, Error: null);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
        {
            // Kept per source rather than aborting: the plugin list is still worth
            // showing when only the appearance catalog is unreachable.
            remoteError = $"{source.What}：{error.Message}";
        }

        foreach (var fallback in new[] { source.CachePath, source.LocalPath })
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fallback) || !File.Exists(fallback)) continue;
                var json = File.ReadAllText(fallback);
                if (json.Length > MaxCatalogBytes) continue;
                var entries = source.Parse(json);
                return new PluginCatalogLoadResult(entries, FromRemote: false, FromCache: true, Error: remoteError);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                remoteError = $"{remoteError}；{error.Message}";
            }
        }

        return new PluginCatalogLoadResult(Array.Empty<PluginCatalogRecord>(), false, false, remoteError ?? $"{source.What}不可用。");
    }

    public static IReadOnlyList<PluginCatalogRecord> Parse(string json)
    {
        var document = JsonSerializer.Deserialize<PluginCatalogDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("插件目录为空。");
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"插件目录版本不兼容：{document.SchemaVersion}。");
        if (document.Plugins is null) throw new InvalidDataException("插件目录缺少 plugins 数组。");
        if (document.Plugins.Count > 100) throw new InvalidDataException("插件目录条目过多。");
        return Filter(document.Plugins);
    }

    /// <summary>
    /// Reads the appearance catalog, refusing a document that does not declare itself.
    /// </summary>
    public static IReadOnlyList<PluginCatalogRecord> ParseAppearances(string json)
    {
        var document = JsonSerializer.Deserialize<AppearanceCatalogDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("形象目录为空。");
        // The file is fetched by URL, and a URL is the one thing that can silently
        // point somewhere else. Its identity is what makes a wrong one visible.
        if (!string.Equals(document.Catalog, AppearanceCatalogIdentity, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"不是形象目录：{document.Catalog}。");
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"形象目录版本不兼容：{document.SchemaVersion}。");
        if (document.Appearances is null) throw new InvalidDataException("形象目录缺少 appearances 数组。");
        if (document.Appearances.Count > 100) throw new InvalidDataException("形象目录条目过多。");
        return Filter(document.Appearances);
    }

    private static IReadOnlyList<PluginCatalogRecord> Filter(List<PluginCatalogRecord> items)
    {
        var result = new List<PluginCatalogRecord>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            Normalize(item);
            if (!Validate(item) || !ids.Add(item.Id)) continue;
            result.Add(item);
        }
        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void Normalize(PluginCatalogRecord item)
    {
        item.Id = (item.Id ?? "").Trim().ToLowerInvariant();
        item.Type = (item.Type ?? "").Trim().ToLowerInvariant();
        item.Name = (item.Name ?? "").Trim();
        item.NameEn = (item.NameEn ?? "").Trim();
        item.Description = (item.Description ?? "").Trim();
        item.DescriptionEn = (item.DescriptionEn ?? "").Trim();
        item.Author = (item.Author ?? "").Trim();
        item.Version = (item.Version ?? "").Trim();
        item.MinCoreVersion = (item.MinCoreVersion ?? "").Trim();
        item.UpdateUrl = (item.UpdateUrl ?? "").Trim();
        item.DownloadUrl = (item.DownloadUrl ?? "").Trim();
        item.Sha256 = (item.Sha256 ?? "").Trim().ToLowerInvariant().Replace("sha256:", "", StringComparison.OrdinalIgnoreCase);
        item.RepositoryUrl = (item.RepositoryUrl ?? "").Trim();
        item.ReleaseUrl = (item.ReleaseUrl ?? "").Trim();
        item.Categories = (item.Categories ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
    }

    private static bool Validate(PluginCatalogRecord item)
    {
        if (item.Type is not ("feature" or "pet" or "theme" or "browser")) return false;
        if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length is < 2 or > 96) return false;
        if (!item.Id.All(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '.' or '-')) return false;
        if (item.Type == "feature" && !item.Id.StartsWith("balancepet.ext.", StringComparison.Ordinal)) return false;
        if (item.Type == "browser" && !item.Id.StartsWith("balancepet.browser.", StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 || item.NameEn.Length > 120) return false;
        if (!IsVersion(item.Version) || (!string.IsNullOrWhiteSpace(item.MinCoreVersion) && !IsVersion(item.MinCoreVersion))) return false;
        if (!IsHttps(item.RepositoryUrl, "github.com") || !IsHttps(item.ReleaseUrl, "github.com")) return false;
        if (item.Type != "browser")
        {
            if (!IsHttps(item.DownloadUrl, "github.com") || !item.DownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
            if (!IsGitHubReleaseAsset(item.DownloadUrl)) return false;
            if (item.Sha256.Length != 64 || !item.Sha256.All(IsHex)) return false;
        }
        if (!string.IsNullOrWhiteSpace(item.UpdateUrl) && !IsGitHubLatestEndpoint(item.UpdateUrl)) return false;
        return true;
    }

    private static bool IsVersion(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(value, "^\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?$");

    private static bool IsHttps(string value, string host)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase);

    private static bool IsGitHubReleaseAsset(string value)
    {
        if (!IsHttps(value, "github.com")) return false;
        var path = new Uri(value).AbsolutePath.Trim('/').Split('/');
        return path.Length >= 6 && !string.IsNullOrWhiteSpace(path[0]) && !string.IsNullOrWhiteSpace(path[1]) &&
               path[2].Equals("releases", StringComparison.OrdinalIgnoreCase) && path[3].Equals("download", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(path[4]) && !string.IsNullOrWhiteSpace(path[5]);
    }

    private static bool IsGitHubLatestEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var path = uri.AbsolutePath.Trim('/').Split('/');
        return path.Length == 5 && path[0].Equals("repos", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path[1]) && !string.IsNullOrWhiteSpace(path[2]) &&
               path[3].Equals("releases", StringComparison.OrdinalIgnoreCase) && path[4].Equals("latest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static void SaveCache(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class PluginCatalogItemView
{
    public PluginCatalogRecord Record { get; }
    public bool IsEnglish { get; }
    public bool IsInstalled { get; }
    public bool HasUpdate { get; }
    public bool IsCompatible { get; }
    public string InstalledVersion { get; }
    public bool IsBusy { get; set; }
    public bool IsRepositoryOnly => Record.Type.Equals("browser", StringComparison.OrdinalIgnoreCase);
    public System.Windows.Visibility ActionVisibility => IsRepositoryOnly ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public string Id => Record.Id;
    public string Name => IsEnglish && !string.IsNullOrWhiteSpace(Record.NameEn) ? Record.NameEn : Record.Name;
    public string Description => IsEnglish && !string.IsNullOrWhiteSpace(Record.DescriptionEn) ? Record.DescriptionEn : Record.Description;
    public string TypeText => Record.Type switch
    {
        "browser" => IsEnglish ? "Browser extension" : "浏览器扩展",
        "feature" => IsEnglish ? "Feature" : "功能扩展",
        "theme" => IsEnglish ? "Theme" : "主题",
        "pet" => IsEnglish ? "Pet" : "资源扩展",
        _ => Record.Type
    };
    public string MetaText => string.Join("  ·  ", new[]
    {
        TypeText,
        string.IsNullOrWhiteSpace(Record.Author) ? "" : (IsEnglish ? $"By {Record.Author}" : $"作者：{Record.Author}"),
        IsEnglish ? $"v{Record.Version}" : $"v{Record.Version}",
        string.IsNullOrWhiteSpace(Record.MinCoreVersion) ? "" : (IsEnglish ? $"Core ≥ {Record.MinCoreVersion}" : $"核心 ≥ {Record.MinCoreVersion}"),
        Record.Categories.Count == 0 ? "" : string.Join(" / ", Record.Categories)
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string StatusText => IsRepositoryOnly
        ? (IsEnglish ? "Browser extension · open its repository to install" : "浏览器扩展 · 请打开仓库安装")
        : !IsCompatible
        ? (IsEnglish ? "Requires a newer BalancePet core" : "需要更新的 BalancePet 核心")
        : HasUpdate
            ? (IsEnglish ? $"Installed v{InstalledVersion} · update available v{Record.Version}" : $"已安装 v{InstalledVersion} · 可更新到 v{Record.Version}")
            : IsInstalled
                ? (IsEnglish ? $"Installed v{InstalledVersion} · up to date" : $"已安装 v{InstalledVersion} · 已是最新版本")
                : (IsEnglish ? "Available from the curated plugin catalog" : "来自官方插件目录，可安全检查后安装");
    public string ActionText => IsRepositoryOnly
        ? (IsEnglish ? "Repository only" : "仅打开仓库")
        : !IsCompatible
        ? (IsEnglish ? "Incompatible" : "不兼容")
        : IsBusy
            ? (IsEnglish ? "Installing…" : "安装中…")
            : HasUpdate
                ? (IsEnglish ? "Update" : "更新")
                : IsInstalled
                    ? (IsEnglish ? "Installed" : "已安装")
                    : (IsEnglish ? "Install" : "安装");
    public bool CanInstall => !IsRepositoryOnly && IsCompatible && !IsBusy && (!IsInstalled || HasUpdate);
    public string InstallTooltip => IsRepositoryOnly
        ? RepositoryText
        : CanInstall ? (IsEnglish ? "Download, verify, and install" : "下载、校验并安装") : StatusText;
    public string RepositoryText => IsEnglish ? "Open repository" : "打开仓库";

    public PluginCatalogItemView(PluginCatalogRecord record, ExtensionCatalogEntry? installed, bool isEnglish)
    {
        Record = record;
        IsEnglish = isEnglish;
        IsInstalled = installed?.IsInstalled == true;
        InstalledVersion = installed?.InstalledVersion ?? "";
        HasUpdate = IsInstalled && ExtensionCatalogEntry.CompareVersions(record.Version, InstalledVersion) > 0;
        IsCompatible = string.IsNullOrWhiteSpace(record.MinCoreVersion) ||
                       Version.TryParse(record.MinCoreVersion.Split('-', '+')[0], out var minimum) && CoreVersion.Current >= minimum;
    }
}
