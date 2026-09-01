[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("start", "stop")]
    [string]$State,

    [Parameter(Position = 1)]
    [string]$TaskId = "",

    [Parameter(Position = 2)]
    [string]$Provider = "generic"
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
Assert-SafeValue "Provider" $providerValue 32
Assert-SafeValue "TaskId" $taskValue 256
if ($State -eq "start" -and [string]::IsNullOrWhiteSpace($taskValue)) {
    throw "TaskId is required for a start event."
}

# The payload contains only lifecycle metadata. Never add prompts, replies,
# credentials, or provider request data here.
$message = @{
    state = $State
    sessionId = "external:$providerValue"
    turnId = $taskValue
    provider = $providerValue
} | ConvertTo-Json -Compress

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
