[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$Provider,
    [string]$Model = "",
    [long]$InputTokens = -1,
    [long]$OutputTokens = -1,
    [long]$CacheReadTokens = -1,
    [long]$CacheWriteTokens = -1,
    [double]$Cost = -1,
    [string]$Currency = "",
    [long]$DurationMs = -1,
    [long]$TimeToFirstTokenMs = -1,
    [int]$ToolCalls = -1,
    [int]$Steps = -1,
    [bool]$Success = $true
)

$ErrorActionPreference = "Stop"
$pipeName = "BalancePet.Usage.v1"
foreach ($value in @($InputTokens, $OutputTokens, $CacheReadTokens, $CacheWriteTokens, $DurationMs, $TimeToFirstTokenMs, $ToolCalls, $Steps)) {
    if ($value -lt -1) { throw "Usage counters must be -1 (not supplied) or non-negative." }
}
if ([string]::IsNullOrWhiteSpace($Provider) -or $Provider.Length -gt 64 -or $Provider -match "\p{C}") { throw "Provider is required and must be at most 64 characters." }
if ($Model.Length -gt 160 -or $Model -match "\p{C}") { throw "Model contains unsupported characters or is too long." }
if ($Cost -lt -1) { throw "Cost must be -1 (not supplied) or non-negative." }
if ($Currency.Length -gt 12 -or $Currency -match "\p{C}") { throw "Currency contains unsupported characters or is too long." }

$message = @{
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
    if ([long]$entry.Value -ge 0) { $message[$entry.Key] = [long]$entry.Value }
}
if ($Cost -ge 0) { $message.cost = $Cost }
if (-not [string]::IsNullOrWhiteSpace($Currency)) { $message.currency = $Currency.Trim().ToUpperInvariant() }
$message = $message | ConvertTo-Json -Compress

$pipe = $null
$writer = $null
try {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(".", $pipeName, [System.IO.Pipes.PipeDirection]::Out, [System.IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(800)
    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $writer.WriteLine($message)
}
catch {
    [Console]::Error.WriteLine("BalancePet is not running or usage events are unavailable.")
    exit 2
}
finally {
    if ($writer) { $writer.Dispose() }
    if ($pipe) { $pipe.Dispose() }
}
