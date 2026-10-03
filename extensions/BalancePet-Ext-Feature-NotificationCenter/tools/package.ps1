[CmdletBinding()]
param(
    # Read from manifest.json rather than defaulted here: the manifest is what the host reads
    # to decide whether to offer an update, so a second copy of the number here can only ever
    # disagree with it — and this one had, by a version.
    [string]$Version = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$extensionRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $extensionRoot "manifest.json") -Raw | ConvertFrom-Json).version
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "无法从 manifest.json 读取版本号。" }
}
$project = Join-Path $extensionRoot "src\BalancePet.NotificationCenter.csproj"
$stage = Join-Path $extensionRoot "dist\balancepet.ext.feature.notification-center-$Version-win-x64"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $extensionRoot "dist\balancepet.ext.feature.notification-center-$Version-win-x64.zip"
}
$output = [System.IO.Path]::GetFullPath($OutputPath)

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null

dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $stage
Copy-Item (Join-Path $extensionRoot "manifest.json") $stage
Copy-Item (Join-Path $extensionRoot "README.md") $stage
Copy-Item (Join-Path $extensionRoot "CHANGELOG.md") $stage
Copy-Item (Join-Path $extensionRoot "LICENSE") $stage
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $output -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created $output"
Write-Host "SHA256 $hash"
