using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Reads only the current user's Chromium cookie database locally. It never
/// sends browser data anywhere; callers receive a short-lived Cookie header
/// and are responsible for protecting it before persistence.
/// </summary>
public sealed class BrowserSessionReader
{
    private readonly DpapiTokenStore _dpapi = new();

    public async Task<BrowserSessionReadResult> ReadAsync(
        string siteUrl,
        string browser,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var site)
            || site.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(site.Host))
            return BrowserSessionReadResult.Failed("中转站地址无效。");

        var browserRoot = ResolveBrowserRoot(browser);
        if (browserRoot is null)
            return BrowserSessionReadResult.Failed($"未找到 {DisplayBrowser(browser)} 用户数据目录。");

        var localState = Path.Combine(browserRoot, "Local State");
        byte[]? masterKey = null;
        try { masterKey = ReadMasterKey(localState); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or FormatException or CryptographicException or System.Security.SecurityException)
        {
            return BrowserSessionReadResult.Failed($"无法读取 {DisplayBrowser(browser)} 加密密钥：{ShortError(error)}");
        }

        foreach (var profile in EnumerateProfiles(browserRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cookiePath = Path.Combine(profile, "Network", "Cookies");
            if (!File.Exists(cookiePath)) continue;
            try
            {
                var result = await Task.Run(() => ReadCookieDatabase(cookiePath, site.Host, masterKey), cancellationToken);
                if (result.CookieCount > 0) return result with { Browser = DisplayBrowser(browser), Profile = Path.GetFileName(profile) };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or CryptographicException or System.Security.SecurityException)
            {
                // Try the next profile. A locked or unsupported profile must
                // not prevent reading a valid session from another profile.
            }
        }

        return BrowserSessionReadResult.Failed($"未找到 {site.Host} 的可用登录 Cookie。浏览器可能使用了新的 App-Bound 加密格式，或当前账户尚未登录。", DisplayBrowser(browser));
    }

    private BrowserSessionReadResult ReadCookieDatabase(string cookiePath, string host, byte[]? masterKey)
    {
        string? tempRoot = null;
        SqliteConnection? connection = null;
        try
        {
            try
            {
                // Chromium keeps the database open while the browser runs,
                // but SQLite still permits a read-only connection. This is
                // the preferred path because copying a live Cookies file is
                // blocked by Windows sharing flags in some browser builds.
                connection = new SqliteConnection($"Data Source={cookiePath};Mode=ReadOnly;Cache=Private");
                connection.Open();
            }
            catch (SqliteException)
            {
                connection?.Dispose();
                tempRoot = Path.Combine(Path.GetTempPath(), "BalancePet", "browser-session", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                var copyPath = Path.Combine(tempRoot, "Cookies");
                File.Copy(cookiePath, copyPath, true);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var sidecar = cookiePath + suffix;
                    if (File.Exists(sidecar)) File.Copy(sidecar, copyPath + suffix, true);
                }
                connection = new SqliteConnection($"Data Source={copyPath};Mode=ReadOnly;Cache=Private");
                connection.Open();
            }

            using (connection)
            {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT host_key, name, value, encrypted_value FROM cookies WHERE host_key = $host OR host_key LIKE $suffix";
            command.Parameters.AddWithValue("$host", host);
            command.Parameters.AddWithValue("$suffix", "%" + host);

            var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var cookieHost = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (!IsDomainMatch(cookieHost, host)) continue;
                var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (name.Length == 0) continue;
                var value = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (value.Length == 0 && !reader.IsDBNull(3))
                {
                    var encrypted = (byte[])reader[3];
                    value = DecryptCookie(encrypted, masterKey);
                }
                if (value.Length > 0) cookies[name] = value;
            }

            if (cookies.Count == 0) return BrowserSessionReadResult.Failed("没有找到可用 Cookie。");
            var header = string.Join("; ", cookies.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            return BrowserSessionReadResult.Succeeded(header, cookies.Count);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    private string DecryptCookie(byte[] encrypted, byte[]? masterKey)
    {
        if (encrypted.Length >= 3 && encrypted[0] == (byte)'v' && encrypted[1] is (byte)'1' or (byte)'2')
        {
            var version = Encoding.ASCII.GetString(encrypted, 0, 3);
            if (version is "v10" or "v11")
            {
                if (masterKey is null || encrypted.Length < 3 + 12 + 16) return "";
                var nonce = encrypted.AsSpan(3, 12).ToArray();
                var tag = encrypted.AsSpan(encrypted.Length - 16, 16).ToArray();
                var cipher = encrypted.AsSpan(15, encrypted.Length - 15 - 16).ToArray();
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(masterKey, 16);
                aes.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            // v20+ is App-Bound Encryption on newer Chromium builds. It cannot
            // be decrypted by a standalone process without the browser's own
            // broker, so report no value and let the caller show a clear hint.
            return "";
        }

        try { return Encoding.UTF8.GetString(_dpapi.UnprotectBytes(encrypted)); }
        catch (CryptographicException) { return ""; }
        catch (System.Security.SecurityException) { return ""; }
    }

    private static byte[]? ReadMasterKey(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("os_crypt", out var osCrypt)
            || !osCrypt.TryGetProperty("encrypted_key", out var value)) return null;
        var encoded = value.GetString();
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        var encrypted = Convert.FromBase64String(encoded);
        if (encrypted.Length > 5 && Encoding.ASCII.GetString(encrypted, 0, 5) == "DPAPI")
            encrypted = encrypted[5..];
        return new DpapiTokenStore().UnprotectBytes(encrypted);
    }

    private static string? ResolveBrowserRoot(string browser)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var relative = browser.Equals("chrome", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine("Google", "Chrome", "User Data")
            : Path.Combine("Microsoft", "Edge", "User Data");
        var root = Path.Combine(local, relative);
        return Directory.Exists(root) ? root : null;
    }

    private static IEnumerable<string> EnumerateProfiles(string root)
    {
        var profiles = new List<string>();
        var defaultProfile = Path.Combine(root, "Default");
        if (Directory.Exists(defaultProfile)) profiles.Add(defaultProfile);
        profiles.AddRange(Directory.EnumerateDirectories(root, "Profile *")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        return profiles;
    }

    private static bool IsDomainMatch(string cookieHost, string host)
        => string.Equals(cookieHost.TrimStart('.'), host, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(cookieHost.TrimStart('.'), StringComparison.OrdinalIgnoreCase);

    private static string DisplayBrowser(string browser)
        => browser.Equals("chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome" : "Edge";

    private static string ShortError(Exception error)
    {
        var text = error.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 120 ? text : text[..120] + "…";
    }
}

public sealed record BrowserSessionReadResult(
    bool Success,
    string CookieHeader,
    int CookieCount,
    string Message,
    string Browser = "",
    string Profile = "")
{
    public static BrowserSessionReadResult Succeeded(string cookieHeader, int cookieCount)
        => new(true, cookieHeader, cookieCount, "已读取当前站点登录 Cookie。");

    public static BrowserSessionReadResult Failed(string message, string browser = "")
        => new(false, "", 0, message, browser);
}
