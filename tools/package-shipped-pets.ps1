# Packages the pet appearances that ship inside the application folder as
# installable resource-extension packages.
#
# Why this exists: appearances are moving out of the distribution. Rather than
# hand-building a manifest and ZIP per pet, this reads the catalogue that already
# describes them and produces one package each, so the set can be regenerated
# whenever the artwork or the catalogue changes.
#
# The `style` value is copied from the built-in style id and must stay that way.
# A user's saved appearance is stored as that id, so a package whose style differs
# would not resolve for anyone upgrading, and their pet would silently fall back
# to the default.
#
# Usage:
#   .\tools\package-shipped-pets.ps1 [-OutputDirectory .\dist\pets] [-Version 1.0.0]
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\pets'),
    [string] $Version = '1.0.0',
    # Appearances that stay in the distribution and therefore get no package.
    [string[]] $Keep = @('deepseek', 'chatgpt'),
    # Package only these ids; empty means every appearance that is not kept. A first
    # publication wants all of them, but republishing one redrawn appearance should
    # not rebuild the other eleven: their packages have not changed, and a new
    # version number would push an update to everyone who already installed them.
    [string[]] $Style = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$catalogPath = Join-Path $root 'versions\csharp-wpf\Services\PetStyleCatalog.cs'
$petsRoot = Join-Path $root 'versions\csharp-wpf\assets\pets'
$packer = Join-Path $PSScriptRoot 'package-pet-extension.ps1'

if (-not (Test-Path $catalogPath)) { throw "找不到形象目录：$catalogPath" }
if (-not (Test-Path $packer)) { throw "找不到打包工具：$packer" }

$requiredStates = @(
    'idle.png', 'loading.png', 'success.png', 'low.png', 'error.png',
    'clicked.png', 'codex-working.png', 'codex-done.png', 'inactive.png'
)

# The catalogue is the single source of truth for ids and display names. English
# names embed escaped quotes (DeepSeek Whale \"Lanxi\"), so the capture allows a
# backslash-escaped character and unescapes it afterwards; a plain [^"]+ stops at
# the backslash and silently truncates the name.
$catalog = Get-Content $catalogPath -Raw
$definitions = [regex]::Matches(
    $catalog,
    'new PetStyleDefinition\("([^"]+)",\s*"([^"]+)",\s*"((?:[^"\\]|\\.)+)"'
) | ForEach-Object {
    [pscustomobject]@{
        Id     = $_.Groups[1].Value
        Name   = $_.Groups[2].Value
        NameEn = $_.Groups[3].Value -replace '\\"', '"'
    }
}

if ($definitions.Count -eq 0) { throw '未能从形象目录中解析出任何定义。' }

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "bp-pet-packages-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$packaged = @()
$skipped = @()
try {
    foreach ($definition in $definitions) {
        if ($Keep -contains $definition.Id) { continue }
        if ($Style.Count -gt 0 -and $Style -notcontains $definition.Id) { continue }

        $source = Join-Path $petsRoot $definition.Id
        if (-not (Test-Path $source)) { $skipped += "$($definition.Id)（无素材目录）"; continue }
        $missing = $requiredStates | Where-Object { -not (Test-Path (Join-Path $source $_)) }
        if ($missing) { $skipped += "$($definition.Id)（缺 $($missing.Count) 张状态图）"; continue }

        $package = Join-Path $stage "pet.$($definition.Id)"
        $assets = Join-Path $package "assets\pets\$($definition.Id)"
        New-Item -ItemType Directory -Path $assets -Force | Out-Null
        Copy-Item (Join-Path $source '*.png') $assets -Force

        $manifest = [ordered]@{
            id              = "pet.$($definition.Id)"
            type            = 'pet'
            name            = $definition.Name
            name_en         = $definition.NameEn
            # Deliberately the built-in id, not the package id: saved settings
            # reference this value.
            style           = $definition.Id
            version         = $Version
            api_version     = 1
            min_core_version = '0.5.0'
        }
        $manifestPath = Join-Path $package 'manifest.json'
        [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 3), [System.Text.UTF8Encoding]::new($false))

        $output = Join-Path $OutputDirectory "pet.$($definition.Id)-$Version.zip"
        & $packer -SourceDirectory $package -OutputPath $output | Out-Null
        if (Test-Path $output) { $packaged += $definition.Id } else { $skipped += "$($definition.Id)（打包失败）" }
    }
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "已打包 $($packaged.Count) 套形象到 $OutputDirectory"
if ($packaged.Count -gt 0) { Write-Host "  $($packaged -join ', ')" }
if ($skipped.Count -gt 0) { Write-Host "跳过 $($skipped.Count) 套：$($skipped -join '；')" }
