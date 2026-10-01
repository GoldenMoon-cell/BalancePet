# Generates the curated plugin catalog entry set for the appearance packages.
#
# Why a generator rather than a checked-in file: the catalog schema requires a
# download URL and a SHA-256 per package, and both are derived from artifacts that
# are built rather than authored. Computing the hashes by hand would drift from the
# packages the first time one is rebuilt, and a catalog whose hash is wrong is
# worse than no catalog, because the install fails after the download.
#
# The repository name is a parameter because it is the one thing that cannot be
# derived. Everything else comes from the packages themselves.
#
# Usage:
#   .\tools\build-skin-catalog.ps1 -Repository GoldenMoon-cell/BalancePet-Pets -ReleaseTag skins-1.0.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Repository,
    # One tag for every entry. Leave empty to derive one per package from its own
    # version, which is what republishing a single appearance needs: the packages
    # that did not change keep pointing at the release that already holds them, and
    # only the republished one moves to a new tag. Deriving it is also what makes a
    # version bump reach installed copies, because the update check compares the
    # catalog's version with the installed manifest's.
    [string] $ReleaseTag = '',
    [string] $TagPrefix = 'skins-',
    [string] $PackagesDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\pets'),
    [string] $OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'skins\catalog.json')
)

$ErrorActionPreference = 'Stop'

if ($Repository -notmatch '^[^/]+/[^/]+$') { throw "仓库应为 owner/name 形式：$Repository" }
if (-not (Test-Path $PackagesDirectory)) { throw "找不到皮肤包目录：$PackagesDirectory" }

$packages = Get-ChildItem $PackagesDirectory -Filter '*.zip' | Sort-Object Name
if ($packages.Count -eq 0) { throw "皮肤包目录里没有 ZIP：$PackagesDirectory" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$releaseRoot = "https://github.com/$Repository/releases"
$appearances = @()
$seenIds = @{}

foreach ($package in $packages) {
    # Names and versions come from the manifest inside the package rather than from
    # the file name, so a renamed file cannot produce an entry that disagrees with
    # what the host will actually read.
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq 'manifest.json' }
        if (-not $entry) { throw "$($package.Name)：包内没有 manifest.json" }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Close() }
    }
    finally { $archive.Dispose() }

    if ($manifest.type -ne 'pet') { throw "$($package.Name)：type 应为 pet，实为 $($manifest.type)" }

    # A rebuilt package left beside the one it replaces is how an appearance ends up
    # with two entries, and a reader would have no way to choose between them. Refuse
    # it here rather than publish a catalog that cannot be acted on.
    if ($seenIds.ContainsKey($manifest.id)) {
        throw "$($package.Name)：id $($manifest.id) 与 $($seenIds[$manifest.id]) 重复。请删除被替换的旧包后重试。"
    }
    $seenIds[$manifest.id] = $package.Name

    $tag = if ($ReleaseTag) { $ReleaseTag } else { "$TagPrefix$($manifest.version)" }

    $appearances += [ordered]@{
        id              = $manifest.id
        type            = 'pet'
        name            = $manifest.name
        name_en         = $manifest.name_en
        version         = $manifest.version
        min_core_version = $manifest.min_core_version
        download_url    = "$releaseRoot/download/$tag/$($package.Name)"
        sha256          = (Get-FileHash $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        repository_url  = "https://github.com/$Repository"
        release_url     = "$releaseRoot/tag/$tag"
        # The selector groups by provider, so the entry says which one it is rather
        # than leaving the user to work it out from the name.
        categories      = @('appearance')
    }
}

$catalog = [ordered]@{
    # An identity, not just a different file name. The main repository's
    # plugin-catalog.json has the same shape -- schema_version, updated_at, one array
    # of entries -- so a reader handed either document has no way to tell which it is
    # holding. This catalog is published from the appearance repository, which
    # describes nothing else, and it says so.
    catalog        = 'balancepet.appearances'
    schema_version = 1
    updated_at     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    appearances    = $appearances
}

New-Item -ItemType Directory -Path (Split-Path $OutputPath -Parent) -Force | Out-Null
[System.IO.File]::WriteAllText($OutputPath, ($catalog | ConvertTo-Json -Depth 5), [System.Text.UTF8Encoding]::new($false))
Write-Host "已写入 $OutputPath（$($appearances.Count) 条）"
$appearances | ForEach-Object { Write-Host "  $($_.id)  $($_.version)  $($_.sha256.Substring(0,12))…" }
