[CmdletBinding()]
param(
    [string]$Version = "1.2.25",
    [switch]$SkipInstaller,
    [string]$StagePath = ""
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "versions\csharp-wpf\BalancePet.Wpf.csproj"
$dist = Join-Path $root "dist"
$stage = if ([string]::IsNullOrWhiteSpace($StagePath)) {
    Join-Path $dist "BalancePet-$Version-win-x64"
} else {
    [System.IO.Path]::GetFullPath($StagePath)
}
$zip = Join-Path $dist "BalancePet-$Version-win-x64.zip"
$installerScript = Join-Path $root "installer\BalancePet.iss"
$setup = Join-Path $dist "BalancePet-$Version-Setup.exe"

if ($Version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)') {
    throw "Version must begin with major.minor.patch: $Version"
}
$versionCore = "$($Matches.major).$($Matches.minor).$($Matches.patch)"
$revision = if ($Version -match '\.(?<revision>\d+)$') { [int]$Matches.revision } else { 0 }
$assemblyVersion = "$versionCore.0"
$fileVersion = "$versionCore.$revision"

# The build tree may carry a Low mandatory integrity label (a sandboxing tool
# can apply one to the repository folder). An executable inheriting that label
# runs at Low integrity, and a Low-integrity process cannot write to %TEMP%.
# Inno Setup then fails with "unable to create the directory ... is-XXXX.tmp"
# (error 5) even though the very same installer works when launched elevated or
# from a copied location. Reset the label on the artifacts users launch.
function Reset-IntegrityLabel {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $label = (icacls $Path 2>&1 | Select-String 'Mandatory Label') -join ''
    if ($label -match 'Low Mandatory') {
        & icacls $Path /setintegritylevel Medium | Out-Null
        Write-Host "Reset Low integrity label on $(Split-Path -Leaf $Path)"
    }
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "C# project was not found: $project"
}
if (-not (Test-Path -LiteralPath $installerScript)) {
    throw "Inno Setup script was not found: $installerScript"
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
if (Test-Path -LiteralPath $setup) {
    Remove-Item -LiteralPath $setup -Force
}

# The installer and portable updater share one complete payload, so either path
# works on a clean Windows installation without a separate .NET runtime setup.
dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $stage `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$fileVersion `
    -p:InformationalVersion=$Version

# Keep the license and attribution next to the executable so binary users see
# the same terms as source users.
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $stage
Copy-Item (Join-Path $root "docs\UPGRADE.md") $stage
Copy-Item (Join-Path $root "plugin-catalog.json") $stage
# Include the optional browser bridge package in the portable/installer payload
# when it has already been built. The main executable does not load the
# extension automatically; this simply keeps the one-time setup package next
# to the app for users who need browser-session based usage details.
$browserBridgeZip = Get-ChildItem (Join-Path $root "extensions\BalancePet-BrowserBridge\dist") -Filter "balancepet.browser-bridge-*.zip" -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -ne $browserBridgeZip) {
    $extensionStage = Join-Path $stage "extensions"
    New-Item -ItemType Directory -Path $extensionStage -Force | Out-Null
    Copy-Item $browserBridgeZip.FullName $extensionStage
}
New-Item -ItemType Directory -Path (Join-Path $stage "tools") -Force | Out-Null
Copy-Item (Join-Path $root "tools\balancepet-task.ps1") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\balancepet-task.cmd") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\balancepet-client-hook.ps1") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\balancepet-usage.ps1") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\install-balancepet-client-hooks.ps1") (Join-Path $stage "tools")
# The DeepSeek Harness bridge is a Cordis plugin rather than a hook script, so
# it ships as a directory that the installer copies into the DSH profile.
Copy-Item (Join-Path $root "tools\install-balancepet-dsh-plugin.ps1") (Join-Path $stage "tools")
# The project file already copies dsh-bridge into the publish output for
# development builds, so clear it first: copying the whole directory over an
# existing one fails, and the release wants the full folder (docs and tools
# included) rather than just the two files the app needs at runtime.
$dshBridgeStage = Join-Path $stage "tools\dsh-bridge"
if (Test-Path -LiteralPath $dshBridgeStage) { Remove-Item -LiteralPath $dshBridgeStage -Recurse -Force }
Copy-Item (Join-Path $root "tools\dsh-bridge") (Join-Path $stage "tools") -Recurse
New-Item -ItemType Directory -Path (Join-Path $stage "docs\licenses") -Force | Out-Null
Copy-Item (Join-Path $root "docs\licenses\MeteorNOX-MIT.txt") (Join-Path $stage "docs\licenses")

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created $zip"
Write-Host "SHA256 $zipHash"

if ($SkipInstaller) {
    Reset-IntegrityLabel -Path $zip
    Write-Warning "Skipped Setup.exe creation. The portable ZIP is suitable for local testing only."
    return
}

$rawInnoCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) # candidate list ends
$innoCandidates = @()
foreach ($candidate in $rawInnoCandidates) {
    if ($null -ne $candidate) {
        $candidatePath = [string]$candidate
        if (-not [string]::IsNullOrWhiteSpace($candidatePath) -and (Test-Path -LiteralPath $candidatePath)) {
            $innoCandidates += $candidatePath
        }
    }
}
$iscc = if ($innoCandidates.Count -gt 0) { $innoCandidates[0] } else { "" }
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw "Inno Setup 6 was not found. Install it, or pass -SkipInstaller when building a portable ZIP only."
}

& $iscc "/DAppVersion=$Version" "/DSourceDir=$stage" "/DOutputDir=$dist" $installerScript
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) {
    throw "Inno Setup failed to create $setup"
}

$setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created $setup"
Write-Host "SHA256 $setupHash"

# The build tree may carry a Low mandatory integrity label (a sandboxing tool
# can apply one to the repository folder). An executable inheriting that label
# runs at Low integrity, and a Low-integrity process cannot write to %TEMP%.
# Inno Setup then fails with "unable to create the directory ... is-XXXX.tmp"
# (error 5) even though the same installer works when run elevated or from a
# copied location. Reset the label on the artifacts users actually launch.
Reset-IntegrityLabel -Path $zip
Reset-IntegrityLabel -Path $setup
