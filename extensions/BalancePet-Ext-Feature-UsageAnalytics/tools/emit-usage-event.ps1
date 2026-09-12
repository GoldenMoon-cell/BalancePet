[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Provider,
    [Parameter(Mandatory = $false)] [string] $Model = "",
    [long] $InputTokens = -1,
    [long] $OutputTokens = -1,
    [long] $CacheReadTokens = -1,
    [long] $CacheWriteTokens = -1,
    [long] $DurationMs = -1,
    [long] $TimeToFirstTokenMs = -1,
    [int] $ToolCalls = -1,
    [int] $Steps = -1,
    [bool] $Success = $true,
    [string] $DataDirectory = ""
)

$ErrorActionPreference = "Stop"
foreach ($value in @($InputTokens, $OutputTokens, $CacheReadTokens, $CacheWriteTokens, $DurationMs, $TimeToFirstTokenMs, $ToolCalls, $Steps)) {
    if ($value -lt -1) { throw "Usage counters must be -1 (not supplied) or non-negative." }
}
if ([string]::IsNullOrWhiteSpace($Provider) -or $Provider.Length -gt 64) { throw "Provider is required and must be at most 64 characters." }
if ($Provider -match "\p{C}") { throw "Provider contains unsupported control characters." }

$directory = if ([string]::IsNullOrWhiteSpace($DataDirectory)) {
    Join-Path $env:LOCALAPPDATA "BalancePet"
} else {
    [System.IO.Path]::GetFullPath($DataDirectory)
}
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$event = [ordered]@{
    schema = "balancepet.usage.v1"
    event_id = [guid]::NewGuid().ToString("N")
    occurred_at = [DateTimeOffset]::Now.ToString("o")
    kind = "llm_request"
    provider = $Provider.Trim()
    model = $Model.Trim()
    success = $Success
}
foreach ($entry in @{
    input_tokens = $InputTokens; output_tokens = $OutputTokens;
    cache_read_tokens = $CacheReadTokens; cache_write_tokens = $CacheWriteTokens;
    duration_ms = $DurationMs; time_to_first_token_ms = $TimeToFirstTokenMs;
    tool_calls = $ToolCalls; steps = $Steps
}.GetEnumerator()) {
    if ([long]$entry.Value -ge 0) { $event[$entry.Key] = [long]$entry.Value }
}
$path = Join-Path $directory "usage-events.ndjson"
[System.IO.File]::AppendAllText($path, (($event | ConvertTo-Json -Compress) + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))
Write-Host "Usage event appended to $path"
