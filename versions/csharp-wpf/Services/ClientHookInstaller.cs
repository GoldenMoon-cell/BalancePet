using System.Diagnostics;
using System.IO;

namespace BalancePet.Wpf.Services;

/// <summary>
/// One AI client whose task lifecycle BalancePet can follow.
/// </summary>
public enum TaskClient
{
    Codex,
    DeepSeekHarness,
    Gemini,
    Qwen,
    Claude,
}

/// <summary>
/// Owns the on-disk integration each client needs. Detection and installed-state
/// checks read files directly so the settings window can render without spawning
/// PowerShell; the bundled scripts run only when something actually changes.
/// </summary>
public static class ClientHookInstaller
{
    private const string ClientHookScript = "install-balancepet-client-hooks.ps1";
    private const string HarnessScript = "install-balancepet-dsh-plugin.ps1";
    private const string HarnessPatchMarker = "balancepet-dsh-bridge";

    private static readonly TimeSpan ScriptTimeout = TimeSpan.FromSeconds(45);

    public static string DisplayName(TaskClient client) => client switch
    {
        TaskClient.Codex => "Codex",
        TaskClient.DeepSeekHarness => "DeepSeek Harness",
        TaskClient.Gemini => "Gemini CLI",
        TaskClient.Qwen => "Qwen Code",
        TaskClient.Claude => "Claude Code",
        _ => client.ToString(),
    };

    /// <summary>
    /// Provider label this client reports, used to identify its hook entries.
    /// Kept in step with <see cref="CodexTaskBridge"/> and the hook scripts.
    /// </summary>
    private static string ProviderOf(TaskClient client) => client switch
    {
        TaskClient.Codex => CodexTaskBridge.CodexProvider,
        TaskClient.DeepSeekHarness => CodexTaskBridge.DeepSeekHarnessProvider,
        TaskClient.Gemini => CodexTaskBridge.GeminiProvider,
        TaskClient.Qwen => CodexTaskBridge.QwenProvider,
        TaskClient.Claude => CodexTaskBridge.ClaudeProvider,
        _ => client.ToString(),
    };

    private static string HomePath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Directory a client creates once it has been used on this machine.</summary>
    public static string? ClientDirectory(TaskClient client) => client switch
    {
        TaskClient.Codex => Path.Combine(HomePath, ".codex"),
        TaskClient.DeepSeekHarness => HarnessHome(),
        TaskClient.Gemini => Path.Combine(HomePath, ".gemini"),
        TaskClient.Qwen => Path.Combine(HomePath, ".qwen"),
        TaskClient.Claude => Path.Combine(HomePath, ".claude"),
        _ => null,
    };

    private static string? HarnessHome()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_HOME");
        return string.IsNullOrWhiteSpace(configured) ? Path.Combine(HomePath, ".dsh") : configured;
    }

    private static string HarnessProfileName()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_PROFILE");
        return string.IsNullOrWhiteSpace(configured) ? "desktop" : configured;
    }

    private static string? ClientSettingsPath(TaskClient client) => client switch
    {
        TaskClient.Gemini => Path.Combine(HomePath, ".gemini", "settings.json"),
        TaskClient.Qwen => Path.Combine(HomePath, ".qwen", "settings.json"),
        TaskClient.Claude => Path.Combine(HomePath, ".claude", "settings.json"),
        _ => null,
    };

    private static string? HarnessPatchPath()
    {
        var home = HarnessHome();
        return home is null ? null : Path.Combine(home, "profiles", HarnessProfileName(), "cordis.patch.yml");
    }

    /// <summary>
    /// True when the client looks installed. Used only for the settings hint:
    /// an absent client can still be switched on, it just gets no hook written.
    /// </summary>
    public static bool IsClientPresent(TaskClient client)
    {
        var directory = ClientDirectory(client);
        return directory is not null && Directory.Exists(directory);
    }

    /// <summary>
    /// Whether BalancePet's hook is registered for this client. Deliberately
    /// file- and text-based: the hook entry names are a stable contract with the
    /// install scripts, and reading them avoids a PowerShell start per render.
    /// </summary>
    public static bool IsInstalled(TaskClient client)
    {
        try
        {
            switch (client)
            {
                case TaskClient.Codex:
                    return CodexHookInstaller.IsInstalled();

                case TaskClient.DeepSeekHarness:
                {
                    var patch = HarnessPatchPath();
                    return patch is not null
                        && File.Exists(patch)
                        && File.ReadAllText(patch).Contains(HarnessPatchMarker, StringComparison.OrdinalIgnoreCase);
                }

                default:
                {
                    var settings = ClientSettingsPath(client);
                    return settings is not null
                        && File.Exists(settings)
                        && File.ReadAllText(settings).Contains($"BalancePet-{ProviderOf(client)}-", StringComparison.Ordinal);
                }
            }
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static bool TryInstall(TaskClient client, out string error)
    {
        if (client == TaskClient.Codex) return CodexHookInstaller.TryInstall(out error);
        if (client == TaskClient.DeepSeekHarness)
            return TryRunScript(HarnessScript, new[] { "-Action", "Install" }, out error);
        return TryRunScript(ClientHookScript, new[] { "-Action", "Install", "-Client", ProviderOf(client) }, out error);
    }

    public static bool TryUninstall(TaskClient client, out string error)
    {
        if (client == TaskClient.Codex) return CodexHookInstaller.TryUninstall(out error);
        if (client == TaskClient.DeepSeekHarness)
            return TryRunScript(HarnessScript, new[] { "-Action", "Uninstall" }, out error);
        return TryRunScript(ClientHookScript, new[] { "-Action", "Remove", "-Client", ProviderOf(client) }, out error);
    }

    /// <summary>
    /// Brings clients in line with their switches: installs where a switch is on
    /// and the client is present. When <paramref name="removeDisabled"/> is set,
    /// a switched-off client also has its hook removed — that is what an explicit
    /// settings save means. A startup refresh passes false so it only repairs and
    /// never deletes an integration the user did not just turn off.
    /// Returns the first failure so the caller can report it; a missing client is
    /// never treated as a failure.
    /// </summary>
    public static bool TrySync(IReadOnlyList<(TaskClient Client, bool Enabled)> wanted, bool removeDisabled, out string error)
    {
        error = "";
        foreach (var (client, enabled) in wanted)
        {
            if (enabled)
            {
                if (!IsClientPresent(client)) continue;
                if (IsInstalled(client)) continue;
                if (!TryInstall(client, out var installError))
                {
                    error = $"{DisplayName(client)}：{installError}";
                    return false;
                }
            }
            else
            {
                if (!removeDisabled) continue;
                if (!IsInstalled(client)) continue;
                if (!TryUninstall(client, out var removeError))
                {
                    error = $"{DisplayName(client)}：{removeError}";
                    return false;
                }
            }
        }
        return true;
    }

    private static bool TryRunScript(string scriptName, string[] arguments, out string error)
    {
        error = "";
        var script = Path.Combine(AppContext.BaseDirectory, "tools", scriptName);
        if (!File.Exists(script))
        {
            error = $"未找到联动脚本：{script}";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
                startInfo.ArgumentList.Add(argument);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "无法启动 powershell.exe";
                return false;
            }

            // Both streams are small (a few status lines); draining them before
            // waiting avoids the deadlock a full pipe buffer would cause.
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)ScriptTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                error = "脚本执行超时";
                return false;
            }

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                detail = detail.Trim();
                error = detail.Length > 0 ? detail : $"退出码 {process.ExitCode}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
