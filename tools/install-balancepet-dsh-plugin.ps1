# Registers (or removes) the BalancePet bridge plugin in a DeepSeek Harness profile.
#
# DeepSeek Harness has no Codex-style hooks.json. Its official extension point is
# a Cordis plugin, so the bridge is mounted through the profile's patch layer:
#
#   1. the plugin directory is copied into the profile directory;
#   2. an `insert` row in cordis.patch.yml mounts it by relative specifier.
#
# The loader resolves a specifier starting with "." against the profile
# directory (@deepseek-ai/cordis-plugin-loader, `import()`), so this needs no
# package manager, no registry access, and no changes to package.json. Pass
# -UsePackageManager to install it as a profile dependency instead.
#
# DeepSeek Harness must be restarted for a change to take effect.

[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Status')]
    [string]$Action = 'Install',

    [string]$Profile = 'desktop',

    [string]$PluginSource = '',

    [string]$DshCli = '',

    [switch]$UsePackageManager
)

$ErrorActionPreference = 'Stop'

$pluginName = 'balancepet-dsh-bridge'
$pluginEntry = "./$pluginName/lib/index.js"
$patchMarker = 'balancepet-dsh-bridge'

function Write-Step($message) { Write-Host "==> $message" }
function Write-Note($message) { Write-Host "    $message" }

function Resolve-DshCli {
    param([string]$Explicit)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { $candidates += $Explicit }
    $candidates += @(
        (Join-Path $env:LOCALAPPDATA 'Programs\DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd'),
        (Join-Path $env:ProgramFiles 'DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd'),
        (Join-Path ${env:ProgramFiles(x86)} 'DeepSeek Harness\resources\runtime\cli\bin\dsh.cmd')
    )
    $onPath = Get-Command 'dsh' -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return ''
}

function Resolve-ProfileDirectory {
    param([string]$Name)

    $dshHome = $env:DSH_HOME
    if ([string]::IsNullOrWhiteSpace($dshHome)) { $dshHome = Join-Path $env:USERPROFILE '.dsh' }
    return (Join-Path $dshHome (Join-Path 'profiles' $Name))
}

function Resolve-PluginSource {
    param([string]$Explicit, [string]$ScriptRoot)

    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        return (Resolve-Path -LiteralPath $Explicit).Path
    }

    # Packaged layout: <app>\tools\install-balancepet-dsh-plugin.ps1 next to
    # <app>\tools\dsh-bridge\. The development layout is identical.
    $candidates = @(
        (Join-Path $ScriptRoot 'dsh-bridge'),
        (Join-Path (Split-Path -Parent $ScriptRoot) 'tools\dsh-bridge')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'package.json')) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return ''
}

function Backup-File {
    param([string]$Path)

    $stamp = Get-Date -Format 'yyyyMMddHHmmss'
    $backup = "$Path.balancepet-backup-$stamp"
    Copy-Item -LiteralPath $Path -Destination $backup -Force
    return $backup
}

# A directory that was just read by another process (an antivirus scan, a
# harness that had the plugin loaded) can refuse deletion for a moment, so
# retry instead of failing the whole uninstall.
function Remove-DirectoryWithRetry {
    param([string]$Path, [int]$Attempts = 5, [int]$DelayMs = 200)

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) { return $true }
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return $true
        } catch {
            if ($attempt -eq $Attempts) {
                Write-Warning "could not remove ${Path}: $($_.Exception.Message)"
                return $false
            }
            Start-Sleep -Milliseconds $DelayMs
        }
    }
    return $false
}

function Test-PatchEntryPresent {
    param([string]$Text)

    return ($Text -match [regex]::Escape($pluginName))
}

function Add-PatchEntry {
    param([string]$Path)

    $text = if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw } else { '' }
    if ([string]::IsNullOrWhiteSpace($text)) { $text = "[]`n" }

    if (Test-PatchEntryPresent -Text $text) {
        Write-Note 'patch entry already present; leaving it as is'
        return $false
    }

    if (Test-Path -LiteralPath $Path) {
        $backup = Backup-File -Path $Path
        Write-Note "backed up to $(Split-Path -Leaf $backup)"
    }

    $specifier = if ($UsePackageManager) { $pluginName } else { $pluginEntry }
    $block = @"
- insert:
    - id: $pluginName
      name: $specifier
"@

    $trimmed = $text.TrimEnd()
    if ($trimmed -eq '[]') {
        # Replace the empty placeholder list instead of appending after it.
        $result = "$block`n"
    } else {
        $result = "$trimmed`n`n$block`n"
    }

    $temp = "$Path.balancepet-tmp"
    Set-Content -LiteralPath $temp -Value $result -Encoding utf8 -NoNewline
    Move-Item -LiteralPath $temp -Destination $Path -Force
    return $true
}

function Remove-PatchEntry {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $lines = @(Get-Content -LiteralPath $Path)
    $kept = @()
    $removed = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*-\s*insert:\s*$' -and
            ($i + 2) -lt $lines.Count -and
            $lines[$i + 2] -match [regex]::Escape($pluginName)) {
            # Skip the three-line block this script writes, plus a following blank.
            $i += 2
            if (($i + 1) -lt $lines.Count -and [string]::IsNullOrWhiteSpace($lines[$i + 1])) { $i++ }
            $removed = $true
            continue
        }
        $kept += $line
    }

    if (-not $removed) { return $false }

    $backup = Backup-File -Path $Path
    Write-Note "backed up to $(Split-Path -Leaf $backup)"
    $temp = "$Path.balancepet-tmp"
    Set-Content -LiteralPath $temp -Value ($kept -join "`n") -Encoding utf8
    Move-Item -LiteralPath $temp -Destination $Path -Force
    return $true
}

# ---------------------------------------------------------------------------

$cli = Resolve-DshCli -Explicit $DshCli
$profileDirectory = Resolve-ProfileDirectory -Name $Profile
$patchFile = Join-Path $profileDirectory 'cordis.patch.yml'
$installedCopy = Join-Path $profileDirectory $pluginName
$source = Resolve-PluginSource -Explicit $PluginSource -ScriptRoot $PSScriptRoot

if ($Action -eq 'Status') {
    Write-Step "DeepSeek Harness CLI: $(if ($cli) { $cli } else { 'not found' })"
    Write-Step "Profile directory: $profileDirectory"
    Write-Note "exists: $(Test-Path -LiteralPath $profileDirectory)"
    Write-Step "Patch file: $patchFile"
    if (Test-Path -LiteralPath $patchFile) {
        Write-Note "bridge entry present: $(Test-PatchEntryPresent -Text (Get-Content -LiteralPath $patchFile -Raw))"
    } else {
        Write-Note 'patch file not found'
    }
    Write-Step "Plugin copy: $installedCopy"
    Write-Note "exists: $(Test-Path -LiteralPath $installedCopy)"
    Write-Step "Plugin source: $(if ($source) { $source } else { 'not found' })"
    return
}

if (-not (Test-Path -LiteralPath $profileDirectory)) {
    throw "DeepSeek Harness profile directory was not found: $profileDirectory"
}

if ($Action -eq 'Install') {
    if ([string]::IsNullOrWhiteSpace($source)) {
        throw 'The bridge plugin source directory was not found. Pass -PluginSource with the path to tools\dsh-bridge.'
    }

    Write-Step "Copying the bridge plugin to $installedCopy"
    Remove-DirectoryWithRetry -Path $installedCopy | Out-Null
    New-Item -ItemType Directory -Force -Path $installedCopy | Out-Null
    foreach ($item in @('package.json', 'lib')) {
        Copy-Item -LiteralPath (Join-Path $source $item) -Destination $installedCopy -Recurse -Force
    }

    if ($UsePackageManager) {
        if ([string]::IsNullOrWhiteSpace($cli)) {
            throw 'The DeepSeek Harness CLI was not found. Pass -DshCli with the full path to dsh.cmd.'
        }
        Write-Step "Installing it into profile '$Profile' as a profile dependency"
        Push-Location $profileDirectory
        try {
            & $cli plugin --profile $Profile add "file:$installedCopy"
            if ($LASTEXITCODE -ne 0) { throw "dsh plugin add failed with exit code $LASTEXITCODE" }
        } finally {
            Pop-Location
        }
    }

    Write-Step 'Mounting it in cordis.patch.yml'
    Add-PatchEntry -Path $patchFile | Out-Null

    Write-Host ''
    Write-Host 'Installed. DeepSeek Harness reloads this profile automatically, so the plugin normally'
    Write-Host 'becomes active without restarting it. Confirm in the log below (look for "bridge active");'
    Write-Host 'if that line is missing, quit DeepSeek Harness from the tray and start it again.'
    Write-Host 'Then enable "自动跟随 DeepSeek Harness" in BalancePet settings.'
    Write-Host 'Diagnostics are written to:'
    Write-Host "  $(Join-Path $env:LOCALAPPDATA 'BalancePet\dsh-bridge.log')"
    return
}

if ($Action -eq 'Uninstall') {
    Write-Step 'Removing the mount from cordis.patch.yml'
    Remove-PatchEntry -Path $patchFile | Out-Null

    if ($UsePackageManager -and -not [string]::IsNullOrWhiteSpace($cli)) {
        Write-Step 'Removing the profile dependency'
        Push-Location $profileDirectory
        try { & $cli plugin --profile $Profile remove $pluginName 2>&1 | Out-Null } finally { Pop-Location }
    }

    Write-Step 'Removing the plugin copy'
    Remove-DirectoryWithRetry -Path $installedCopy | Out-Null

    Write-Host ''
    Write-Host 'Uninstalled. Restart DeepSeek Harness so the plugin is no longer loaded.'
}
