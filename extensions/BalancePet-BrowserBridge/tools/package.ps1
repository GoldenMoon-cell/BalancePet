[CmdletBinding()]
param([string]$Version = "1.0.0")

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "balancepet.browser-bridge-$Version"
$output = Join-Path $dist "balancepet.browser-bridge-$Version.zip"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $root "manifest.json") $stage
Copy-Item (Join-Path $root "popup.html") $stage
Copy-Item (Join-Path $root "popup.js") $stage
Copy-Item (Join-Path $root "capture-main.js") $stage
Copy-Item (Join-Path $root "background.js") $stage
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $output -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created $output"
Write-Host "SHA256 $hash"
