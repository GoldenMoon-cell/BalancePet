param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$manifestPath = Join-Path $source 'manifest.json'
$themePath = Join-Path $source 'theme.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Theme source is missing manifest.json.' }
if (-not (Test-Path -LiteralPath $themePath)) { throw 'Theme source is missing theme.json.' }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$theme = Get-Content -LiteralPath $themePath -Raw | ConvertFrom-Json
if ($manifest.type -ne 'theme') { throw 'manifest.json type must be theme.' }
if ($manifest.api_version -ne 1) { throw 'manifest.json api_version must be 1.' }
if ($manifest.theme_file -ne 'theme.json') { throw 'manifest.json theme_file must be theme.json.' }
if ($theme.schema_version -ne 1) { throw 'theme.json schema_version must be 1.' }
if ($theme.preferred_backdrop -notin @('mica', 'mica-alt', 'acrylic', 'solid')) { throw 'theme.json preferred_backdrop is invalid.' }

$files = Get-ChildItem -LiteralPath $source -File -Recurse
if ($files.Count -gt 16) { throw 'Theme extensions may contain at most 16 files.' }
foreach ($file in $files) {
    if ($file.Extension -notin @('.json', '.png')) { throw "Theme extensions cannot contain $($file.Extension) files." }
}

$destination = [System.IO.Path]::GetFullPath($OutputPath)
$destinationDirectory = Split-Path -Parent $destination
if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) { New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null }
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $destination -CompressionLevel Optimal

$size = (Get-Item -LiteralPath $destination).Length
if ($size -gt 4MB) { Remove-Item -LiteralPath $destination -Force; throw 'Theme package exceeds 4 MB.' }
Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Select-Object Path, Hash
