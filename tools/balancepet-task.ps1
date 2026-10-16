[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("start", "stop")]
    [string]$State,

    [Parameter(Position = 1)]
    [string]$TaskId = "",

    [Parameter(Position = 2)]
    [string]$Provider = "generic",

    # The balance account this task is billed to. It is the only attribution a
    # client can offer: the account name or id the user configured in BalancePet.
    # Empty means "the client does not know", which the host reports as unassigned
    # rather than charging whichever account happens to be current.
    [string]$Account = "",

    [string]$Model = "",
    [long]$InputTokens = -1,
    [long]$OutputTokens = -1,
    [long]$CacheReadTokens = -1,
    [long]$CacheWriteTokens = -1,
    [long]$DurationMs = -1,
    [long]$TimeToFirstTokenMs = -1,
    [long]$ToolCalls = -1,
    [long]$Steps = -1,
    [bool]$Success = $true
)

$ErrorActionPreference = "Stop"
$pipeName = "BalancePet.Task.v1"

function Assert-SafeValue([string]$Name, [string]$Value, [int]$MaximumLength) {
    if ($Value.Length -gt $MaximumLength -or $Value -match "\p{C}") {
        throw "$Name contains unsupported characters or is too long."
    }
}

$providerValue = if ([string]::IsNullOrWhiteSpace($Provider)) { "generic" } else { $Provider.Trim() }
$taskValue = if ($null -eq $TaskId) { "" } else { $TaskId.Trim() }
$accountValue = if ($null -eq $Account) { "" } else { $Account.Trim() }
Assert-SafeValue "Provider" $providerValue 32
Assert-SafeValue "TaskId" $taskValue 256
Assert-SafeValue "Model" $Model 160
Assert-SafeValue "Account" $accountValue 128
if ($State -eq "start" -and [string]::IsNullOrWhiteSpace($taskValue)) {
    throw "TaskId is required for a start event."
}
foreach ($value in @($InputTokens, $OutputTokens, $CacheReadTokens, $CacheWriteTokens, $DurationMs, $TimeToFirstTokenMs, $ToolCalls, $Steps)) {
    if ($value -lt -1) { throw "Usage counters must be -1 (not supplied) or non-negative." }
}

# The payload contains lifecycle metadata plus optional non-sensitive counters
# and timings. Never add prompts, replies, credentials, or provider request
# data here.
$message = [ordered]@{
    state = $State
    sessionId = "external:$providerValue"
    turnId = $taskValue
    provider = $providerValue
}
# Omitted entirely rather than sent empty: an absent account_id means "this client
# makes no claim", which the host must not confuse with a claim that failed to
# resolve. The distinction is what lets the menu say unassigned instead of unknown.
if (-not [string]::IsNullOrWhiteSpace($accountValue)) { $message.account_id = $accountValue }
$optionalUsage = @{
    model = $Model
    input_tokens = $InputTokens
    output_tokens = $OutputTokens
    cache_read_tokens = $CacheReadTokens
    cache_write_tokens = $CacheWriteTokens
    duration_ms = $DurationMs
    time_to_first_token_ms = $TimeToFirstTokenMs
    tool_calls = $ToolCalls
    steps = $Steps
}
foreach ($entry in $optionalUsage.GetEnumerator()) {
    if ($entry.Key -eq 'model' -and -not [string]::IsNullOrWhiteSpace([string]$entry.Value)) { $message[$entry.Key] = [string]$entry.Value }
    elseif ($entry.Key -ne 'model' -and [long]$entry.Value -ge 0) { $message[$entry.Key] = [long]$entry.Value }
}
if ($State -eq 'stop') { $message.success = $Success }
$message = $message | ConvertTo-Json -Compress

$attempts = if ($State -eq "stop") { 2 } else { 3 }
$timeoutMs = if ($State -eq "stop") { 300 } else { 800 }
$sent = $false
for ($attempt = 0; $attempt -lt $attempts -and -not $sent; $attempt++) {
    $pipe = $null
    $writer = $null
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            $pipeName,
            [System.IO.Pipes.PipeDirection]::Out,
            [System.IO.Pipes.PipeOptions]::Asynchronous
        )
        $pipe.Connect($timeoutMs)
        $writer = [System.IO.StreamWriter]::new(
            $pipe,
            [System.Text.UTF8Encoding]::new($false)
        )
        $writer.AutoFlush = $true
        $writer.WriteLine($message)
        $sent = $true
    }
    catch {
        if ($attempt -lt ($attempts - 1)) {
            Start-Sleep -Milliseconds 80
        }
    }
    finally {
        if ($writer) { $writer.Dispose() }
        if ($pipe) { $pipe.Dispose() }
    }
}

if (-not $sent) {
    [Console]::Error.WriteLine("BalancePet is not running or task integration is disabled.")
    exit 2
}
