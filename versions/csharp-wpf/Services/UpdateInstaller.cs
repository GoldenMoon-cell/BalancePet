using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BalancePet.Wpf.Services;

public static class UpdateInstaller
{
    public static bool TryCreatePlan(UpdateRelease release, string targetDirectory, out UpdateInstallPlan? plan, out string error)
    {
        plan = null;
        error = "";
        if (CanWriteToDirectory(targetDirectory))
        {
            if (release.PortableArchive is not null)
            {
                plan = new UpdateInstallPlan(UpdateInstallMethod.PortableArchive, release.PortableArchive);
                return true;
            }

            if (release.Installer is not null)
            {
                plan = new UpdateInstallPlan(UpdateInstallMethod.Installer, release.Installer);
                return true;
            }

            error = "该版本没有可用的更新文件。";
            return false;
        }

        if (release.Installer is not null)
        {
            plan = new UpdateInstallPlan(UpdateInstallMethod.Installer, release.Installer);
            return true;
        }

        error = "当前安装目录需要管理员权限，但该版本未提供安装器更新包。请下载 Setup.exe 后手动更新。";
        return false;
    }

    public static bool CanWriteToDirectory(string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory)) return false;
        var probe = Path.Combine(targetDirectory, $".balancepet-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }

    public static bool TryLaunchArchive(string archivePath, string targetDirectory, int processId, string targetVersion, out string error)
    {
        error = "";
        if (!File.Exists(archivePath)) { error = "找不到已下载的更新包。"; return false; }
        if (!Directory.Exists(targetDirectory)) { error = "找不到当前程序目录。"; return false; }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"BalancePet-updater-{Guid.NewGuid():N}.ps1");
        var script = $$"""
$ErrorActionPreference = 'Stop'
$archive = '{{Quote(archivePath)}}'
$target = '{{Quote(targetDirectory)}}'
$processId = {{processId}}
$version = '{{Quote(targetVersion)}}'
$scriptPath = $PSCommandPath
try {
    while (Get-Process -Id $processId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }
    $extract = Join-Path ([System.IO.Path]::GetTempPath()) ('BalancePet-extract-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
    Get-ChildItem -LiteralPath $extract -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name) -Recurse -Force
    }
    Remove-Item -LiteralPath $extract -Recurse -Force
    Remove-Item -LiteralPath $archive -Force
    Start-Process -FilePath (Join-Path $target 'BalancePet.Wpf.exe') -ArgumentList @('--updated-to', $version)
} catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show(('BalancePet update failed: ' + $_.Exception.Message), 'BalancePet') | Out-Null
} finally {
    Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
}
""";

        try
        {
            // Windows PowerShell 5.1 needs a BOM to reliably parse non-ASCII install paths.
            File.WriteAllText(scriptPath, script, new UTF8Encoding(true));
            var updater = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (updater is null)
            {
                error = "无法启动更新程序。";
                try { File.Delete(scriptPath); } catch (IOException) { }
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            try { File.Delete(scriptPath); } catch (IOException) { }
            return false;
        }
    }

    public static bool TryLaunchInstaller(string installerPath, string targetDirectory, out string error)
    {
        error = "";
        if (!File.Exists(installerPath)) { error = "找不到已下载的安装器。"; return false; }

        try
        {
            var installer = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"/ALLUSERS /DIR=\"{QuoteArgument(targetDirectory)}\" /CLOSEAPPLICATIONS",
                UseShellExecute = true,
                Verb = "runas"
            });
            if (installer is null)
            {
                error = "无法启动安装器。";
                return false;
            }
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            error = "已取消管理员授权，当前版本不会被修改。";
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string QuoteArgument(string value) => value.Replace("\"", "\\\"", StringComparison.Ordinal);
}

public enum UpdateInstallMethod
{
    PortableArchive,
    Installer
}

public sealed record UpdateInstallPlan(UpdateInstallMethod Method, UpdateAsset Asset);
