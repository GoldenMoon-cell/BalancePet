[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SourceDirectory,
    [Parameter(Mandatory = $true)] [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$manifestPath = Join-Path $source 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw '扩展根目录缺少 manifest.json。' }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.type -ne 'pet') { throw '当前打包脚本只支持 type=pet。' }
if ($manifest.id -notmatch '^[a-z0-9][a-z0-9.-]{0,62}[a-z0-9]$') { throw 'manifest.id 格式无效。' }
if ($manifest.style -notmatch '^[a-z0-9][a-z0-9.-]{0,62}[a-z0-9]$') { throw 'manifest.style 格式无效。' }
if ($manifest.version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') { throw 'manifest.version 必须是 x.y.z。' }
if ([int]$manifest.api_version -ne 1) { throw 'manifest.api_version 必须是 1。' }

$states = @('idle.png','loading.png','success.png','low.png','error.png','clicked.png','codex-working.png','codex-done.png','inactive.png')
$assetRoot = Join-Path $source (Join-Path 'assets\pets' $manifest.style)
foreach ($state in $states) {
    if (-not (Test-Path -LiteralPath (Join-Path $assetRoot $state) -PathType Leaf)) { throw "缺少状态图：$state。" }
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $output
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $output -CompressionLevel Optimal
Write-Host "已创建扩展包：$output"
