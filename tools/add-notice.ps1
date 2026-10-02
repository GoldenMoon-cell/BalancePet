# Drafts the next changelog notice.
#
# Why a draft rather than a generator: a file hash can say that something changed,
# it cannot say what. That sentence is the whole message, so it has to be written by
# someone who knows what they changed. What can be automated is finding out *which*
# commits happened since the last notice and what their subjects were, which is the
# part that requires remembering.
#
# Run under PowerShell 7 (pwsh). The file is UTF-8 without a BOM like its neighbours,
# and Windows PowerShell 5.1 decodes those as ANSI and mangles the Chinese text.
#
# Usage:
#   .\tools\add-notice.ps1                      # commits since notices.json last changed
#   .\tools\add-notice.ps1 -Since v1.4.5        # or since a tag
[CmdletBinding()]
param(
    [string] $NoticesPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'notices.json'),
    # Anything git can resolve. Defaults to the commit that last touched notices.json.
    [string] $Since = '',
    # Restricts the listing to the paths a notice is usually about. A release bumps the
    # project file and the README; those are announced by the release, not here.
    [string[]] $Paths = @('docs', 'tools', 'skins', 'notices.json', 'README.md', 'AGENTS.md')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not (Test-Path $NoticesPath)) { throw "找不到通告文件：$NoticesPath" }

$document = Get-Content -LiteralPath $NoticesPath -Raw | ConvertFrom-Json
$existing = @($document.notices)
$nextSeq = if ($existing.Count -eq 0) { 1 } else { ($existing | Measure-Object -Property seq -Maximum).Maximum + 1 }

if ([string]::IsNullOrWhiteSpace($Since)) {
    $Since = (git -C $root log -1 --format=%H -- $NoticesPath)
    if ([string]::IsNullOrWhiteSpace($Since)) { $Since = 'HEAD~20' }
}

Write-Host "上一条通告之后的改动（$Since..HEAD）："
Write-Host ''
$commits = git -C $root log "$Since..HEAD" --no-merges --format='%h%x09%s' -- $Paths
if (-not $commits) {
    Write-Host '  （没有）——如果确实改了什么，试试 -Since 指定更早的起点。'
}
foreach ($line in $commits) {
    $parts = $line -split "`t", 2
    Write-Host ("  {0}  {1}" -f $parts[0], $parts[1])
}

Write-Host ''
Write-Host '把下面这段填好后放进 notices.json 的 notices 数组开头（summary 是最需要你写的一句）：'
Write-Host ''
Write-Host (@'
    {
      "seq": __SEQ__,
      "date": "__DATE__",
      "area": "规范",
      "title": "",
      "summary": "",
      "url": "https://github.com/GoldenMoon-cell/BalancePet/commit/__SHA__"
    },
'@ -replace '__SEQ__', $nextSeq -replace '__DATE__', (Get-Date -Format 'yyyy-MM-dd') -replace '__SHA__', '在此填入最相关的那条提交')
Write-Host ''
Write-Host '写完后校验：python tools\validate-spec-documents.py'
