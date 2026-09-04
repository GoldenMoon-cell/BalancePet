[CmdletBinding()]
param(
    [string]$Version = "0.3.2",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "versions\csharp-wpf\BalancePet.Wpf.csproj"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "BalancePet-$Version-win-x64"
$zip = Join-Path $dist "BalancePet-$Version-win-x64.zip"
$installerScript = Join-Path $root "installer\BalancePet.iss"
$setup = Join-Path $dist "BalancePet-$Version-Setup.exe"

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
dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $stage

# Keep the license and attribution next to the executable so binary users see
# the same terms as source users.
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $stage
Copy-Item (Join-Path $root "docs\UPGRADE.md") $stage
New-Item -ItemType Directory -Path (Join-Path $stage "tools") -Force | Out-Null
Copy-Item (Join-Path $root "tools\balancepet-task.ps1") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\balancepet-task.cmd") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\balancepet-client-hook.ps1") (Join-Path $stage "tools")
Copy-Item (Join-Path $root "tools\install-balancepet-client-hooks.ps1") (Join-Path $stage "tools")
New-Item -ItemType Directory -Path (Join-Path $stage "docs\licenses") -Force | Out-Null
Copy-Item (Join-Path $root "docs\licenses\MeteorNOX-MIT.txt") (Join-Path $stage "docs\licenses")

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created $zip"
Write-Host "SHA256 $zipHash"

if ($SkipInstaller) {
    Write-Warning "Skipped Setup.exe creation. The portable ZIP is suitable for local testing only."
    return
}

$innoCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }
$iscc = $innoCandidates | Select-Object -First 1
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
