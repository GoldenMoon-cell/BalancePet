# Mirrors one of this project's GitHub releases to Gitee.
#
# Why it exists: the update package is fifty to eighty megabytes and it is the one
# download with no other way round a network that refuses GitHub. Measured on such a
# network: three public mirrors cut every connection at the same three-second mark,
# jsDelivr does not serve release assets at all and caps a file at twenty megabytes, and
# GitHub's own asset endpoint answers 403 without a token because the unauthenticated
# allowance is sixty requests an hour. The program therefore looks for its update on a
# domestic mirror first (see DownloadMirror.cs) and falls back to GitHub.
#
# This script is what puts the files there, and what checks that they can actually be
# fetched the way the program fetches them: anonymously, and in ranges.
#
# The token is read from a file rather than a parameter, and is never printed. Gitee's
# repository-scoped tokens cannot create a repository, so the repository is made by hand
# once; everything after that is here.
#
# Usage:
#   .\tools\mirror-release-to-gitee.ps1 -Tag v1.5.0
#   .\tools\mirror-release-to-gitee.ps1 -Tag v1.5.0 -DryRun
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Tag,
    [string] $Owner = 'GoldenMoon-cell',
    [string] $Repo = 'balancepet',
    [string] $TokenPath = '',
    [string] $AssetsDirectory = '',
    # Leaves older releases' attachments alone. The default reclaims them, because two
    # assets are 119 MB and a free repository holds about a gigabyte: eight releases and
    # the quota is gone. Only the newest version is ever downloaded.
    [switch] $KeepOld,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $TokenPath) { $TokenPath = Join-Path $root 'dist\.gitee-token' }
if (-not $AssetsDirectory) { $AssetsDirectory = Join-Path $root 'dist' }

$api = "https://gitee.com/api/v5/repos/$Owner/$Repo"
$web = "https://gitee.com/$Owner/$Repo/releases/download/$Tag"

if (-not (Test-Path $TokenPath)) { throw "找不到令牌文件：$TokenPath" }
$token = (Get-Content -LiteralPath $TokenPath -Raw).Trim()
if ($token.Length -eq 0) { throw "令牌文件是空的：$TokenPath" }

$version = $Tag.TrimStart('v', 'V')
$wanted = @(
    "BalancePet-$version-Setup.exe",
    "BalancePet-$version-win-x64.zip"
)

# ---------------------------------------------------------------- the assets

New-Item -ItemType Directory -Path $AssetsDirectory -Force | Out-Null
foreach ($name in $wanted) {
    $path = Join-Path $AssetsDirectory $name
    if (Test-Path $path) { continue }
    Write-Host "本地没有 $name，从 GitHub 取一份…"
    # gh is authenticated already, and going through it means the mirror carries exactly
    # what the release carries rather than something rebuilt beside it.
    gh release download $Tag --repo "$Owner/BalancePet" --pattern $name --dir $AssetsDirectory --clobber
    if (-not (Test-Path $path)) { throw "取不到 $name" }
}
$assets = $wanted | ForEach-Object {
    $path = Join-Path $AssetsDirectory $_
    [pscustomobject]@{ Name = $_; Path = $path; Bytes = (Get-Item $path).Length }
}
foreach ($asset in $assets) {
    Write-Host ("  {0,-34} {1,8:N1} MB" -f $asset.Name, ($asset.Bytes / 1MB))
}

# ---------------------------------------------------------------- the release

function Invoke-Gitee {
    param([string] $Method, [string] $Uri, [object] $Body)
    # Gitee takes the token as an access_token parameter rather than in an Authorization
    # header — that is GitHub's form, and borrowing it here earns a 401 that reads like a
    # bad token instead of like a wrong request.
    $separator = if ($Uri.Contains('?')) { '&' } else { '?' }
    $withToken = "$Uri$separator" + "access_token=$token"
    if ($Body) {
        return Invoke-RestMethod -Method $Method -Uri $withToken -Body $Body -ContentType 'application/json'
    }
    return Invoke-RestMethod -Method $Method -Uri $withToken
}

$release = $null
try {
    $found = Invoke-Gitee GET "$api/releases/tags/$Tag"
    # Checked for an id rather than for "not null": a tag that does not exist comes back as
    # an empty value rather than as an error on this endpoint, and treating that as a hit
    # reports a release that is not there.
    if ($found -and $found.id) {
        $release = $found
        Write-Host "Gitee 上已有 Release $Tag（id=$($release.id)）"
    }
} catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -ne 404) { throw "读取 Release 失败（HTTP $status）：$($_.Exception.Message)" }
}

if (-not $release) {
    if ($DryRun) {
        Write-Host "[dry-run] 会新建 Release $Tag"
    } else {
        $repository = Invoke-Gitee GET "https://gitee.com/api/v5/repos/$Owner/$Repo"
        $release = Invoke-Gitee POST "$api/releases" (@{
            tag_name         = $Tag
            name             = "BalancePet $Tag"
            body             = "BalancePet $Tag 的国内镜像。文件与 GitHub Release 上的一致，SHA-256 以 GitHub Release 页面为准。"
            target_commitish = $repository.default_branch
        } | ConvertTo-Json)
        Write-Host "已新建 Release $Tag（id=$($release.id)，分支 $($repository.default_branch)）"
    }
}

# ---------------------------------------------------------------- upload

function Send-Attachment {
    param([string] $ReleaseId, [string] $FilePath)
    # curl with a config file rather than -F on the command line: an argument list is
    # readable by every process of this user, and the token has no business being there.
    # Written beside the token rather than in the temp directory, for the same reason that
    # directory is not used for downloads: it is not reliably writable, and a file holding
    # a credential is better off somewhere that is already ignored by git.
    $folder = Split-Path -Parent (Resolve-Path $TokenPath)
    $config = Join-Path $folder ("gitee-upload-{0}.cfg" -f [guid]::NewGuid().ToString('N'))
    try {
        # Forward slashes in the path: a curl config file treats a backslash as an escape,
        # so "C:\Users\..." is read as escapes and the upload fails with "failed to open
        # local data" — which reads like a missing file rather than like a quoting problem.
        $upload = $FilePath -replace '\\', '/'
        @(
            "url = `"$api/releases/$ReleaseId/attach_files`""
            "form = `"file=@$upload`""
            "form = `"access_token=$token`""
            "silent"
            "show-error"
        ) | Set-Content -LiteralPath $config -Encoding utf8
        $result = & curl.exe --config $config 2>&1
        if ($LASTEXITCODE -ne 0) { throw "上传失败：$result" }
        return ($result | ConvertFrom-Json)
    } finally {
        Remove-Item -LiteralPath $config -Force -ErrorAction SilentlyContinue
    }
}

$existing = @()
if ($release) {
    try { $existing = @(Invoke-Gitee GET "$api/releases/$($release.id)/attach_files") } catch { $existing = @() }
}

foreach ($asset in $assets) {
    $same = $existing | Where-Object { $_.name -eq $asset.Name }
    if ($same) {
        if ($DryRun) { Write-Host "[dry-run] 会先删掉同名旧附件 $($asset.Name)"; continue }
        foreach ($old in $same) {
            Invoke-Gitee DELETE "$api/releases/$($release.id)/attach_files/$($old.id)" | Out-Null
            Write-Host "  删掉同名旧附件 $($asset.Name)"
        }
    }
    if ($DryRun) { Write-Host "[dry-run] 会传 $($asset.Name)"; continue }
    $uploaded = Send-Attachment -ReleaseId $release.id -FilePath $asset.Path
    Write-Host ("  已传 {0}（id={1}）" -f $asset.Name, $uploaded.id)
}

# ---------------------------------------------------------------- old releases

if (-not $KeepOld) {
    $all = @(Invoke-Gitee GET "$api/releases?per_page=100")
    foreach ($old in $all) {
        if ($old.tag_name -eq $Tag) { continue }
        $attachments = @()
        try { $attachments = @(Invoke-Gitee GET "$api/releases/$($old.id)/attach_files") } catch { }
        foreach ($attachment in $attachments) {
            if ($DryRun) { Write-Host "[dry-run] 会回收 $($old.tag_name) 的附件 $($attachment.name)"; continue }
            Invoke-Gitee DELETE "$api/releases/$($old.id)/attach_files/$($attachment.id)" | Out-Null
            Write-Host "  回收旧版本附件 $($old.tag_name) / $($attachment.name)"
        }
    }
}

# ---------------------------------------------------------------- verify

# Checked the way the program fetches them: no token, and then in a range, because a
# mirror that cannot answer a range request cannot be resumed from — and resuming is the
# whole reason a seventy megabyte download arrives at all on the network this is for.
if ($DryRun) { Write-Host "[dry-run] 不验证"; return }

foreach ($asset in $assets) {
    $url = "$web/$($asset.Name)"
    # -L because the published address is a redirect chain: the releases path, then the
    # attachment path, then a signed CDN address. Without it every check reads a 302 and
    # reports a mirror that does not work when it does.
    $head = & curl.exe -sS -L -o NUL -w "%{http_code} %{size_download}" --max-time 600 $url 2>&1
    $parts = $head -split '\s+'
    $ok = $parts[0] -eq '200' -and [int64]$parts[1] -eq $asset.Bytes
    Write-Host ("  匿名整包下载 {0}：HTTP {1}，{2:N1} MB {3}" -f $asset.Name, $parts[0], ([int64]$parts[1] / 1MB), $(if ($ok) { '✅' } else { '❌ 与 GitHub 上的大小不一致' }))

    $range = & curl.exe -sS -L -o NUL -w "%{http_code} %{size_download}" --max-time 300 -r 0-1048575 $url 2>&1
    $rparts = $range -split '\s+'
    if ($rparts[0] -eq '206' -and [int64]$rparts[1] -eq 1048576) {
        Write-Host "  Range 请求 1 MB：HTTP 206 ✅ 可以续传"
    } elseif ($rparts[0] -eq '200') {
        # Measured: Gitee answers a range request with the whole file. The program notices
        # this for itself and stops retrying a host that cannot continue a transfer, then
        # hands the bytes it did get to GitHub, which can.
        Write-Host ("  Range 请求 1 MB：HTTP 200，回整个文件（{0:N1} MB）—— 这个镜像不支持续传" -f ([int64]$rparts[1] / 1MB))
        Write-Host "     后果：镜像上断一次就要整包重来；程序会带着已收到的字节回退到 GitHub 续完。"
    } else {
        Write-Host ("  Range 请求：HTTP {0} ⚠️ 未知行为" -f $rparts[0])
    }
}

Write-Host ""
Write-Host "镜像就绪。程序里把 DownloadMirror.Base 设为："
Write-Host "  https://gitee.com/$Owner/$Repo/releases/download"
