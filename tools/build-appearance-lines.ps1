# Collects every appearance's lines into the single document the appearance
# repository serves, so correcting what a character says is one small commit
# instead of a new package version.
#
# Why this way round: the lines used to travel only inside the package, and a
# package is mostly artwork. Fixing a word meant republishing an appearance and
# pushing everyone through a download of several megabytes to deliver a few
# hundred bytes -- across the published set that was 147 MB of transfer for
# 36 KB of text. The copy inside the package stays as the offline fallback; this
# document is what the application prefers when it can reach the network.
#
# The placeholder is left out on purpose. It is not appearance content: it ships
# inside the program, its file is always present, and it is deliberately not
# published as a package.
#
# Usage:
#   .\tools\build-appearance-lines.ps1
[CmdletBinding()]
param(
    [string] $PetsDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'versions\csharp-wpf\assets\pets'),
    [string] $OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'skins\lines.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PetsDirectory)) { throw "找不到形象素材目录：$PetsDirectory" }

$SchemaVersion = 2
$lines = [ordered]@{}
$skipped = @()

foreach ($directory in Get-ChildItem $PetsDirectory -Directory | Sort-Object Name) {
    # The placeholder, and anything else the project does not publish.
    if ($directory.Name.StartsWith('_') -or $directory.Name.StartsWith('.')) { continue }

    $file = Join-Path $directory.FullName 'lines.json'
    if (-not (Test-Path $file)) { $skipped += "$($directory.Name)（无 lines.json）"; continue }

    # Read and re-emit rather than copying the text: the document has to be valid
    # JSON as a whole, and a per-appearance file with a stray character would
    # otherwise take every other appearance's lines down with it.
    try {
        $parsed = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    }
    catch {
        $skipped += "$($directory.Name)（lines.json 解析失败）"
        continue
    }

    # schema_version belongs to the document, not to each entry: a nested copy
    # would be a second value that can disagree with the outer one.
    $parsed.PSObject.Properties.Remove('schema_version')
    $lines[$directory.Name] = $parsed
}

if ($lines.Count -eq 0) { throw '没有收集到任何台词，拒绝写出空文档。' }

$document = [ordered]@{
    schema_version = $SchemaVersion
    lines          = $lines
}

New-Item -ItemType Directory -Path (Split-Path $OutputPath -Parent) -Force | Out-Null
[System.IO.File]::WriteAllText($OutputPath, ($document | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
Write-Host "已写入 $OutputPath（$($lines.Count) 套形象，$((Get-Item $OutputPath).Length) 字节）"
$lines.Keys | ForEach-Object { Write-Host "  $_" }
if ($skipped.Count -gt 0) { Write-Host "跳过 $($skipped.Count) 套：$($skipped -join '；')" }
