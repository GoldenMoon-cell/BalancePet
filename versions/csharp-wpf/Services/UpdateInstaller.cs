using System.Diagnostics;
using System.IO;
using System.Text;

namespace BalancePet.Wpf.Services;

public static class UpdateInstaller
{
    public static bool TryLaunch(string archivePath, string targetDirectory, int processId, out string error)
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
    Start-Process -FilePath (Join-Path $target 'BalancePet.Wpf.exe')
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

    private static string Quote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
