[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("login", "logout")]
    [string]$State,

    [Parameter(Mandatory = $true, Position = 1)]
    [ValidatePattern("^[A-Za-z0-9 ._-]{1,32}$")]
    [string]$Provider,

    [Parameter(Position = 2)]
    [ValidateSet("official", "official-api", "relay-api", "third-party", "unknown")]
    [string]$AccountType = "unknown",

    [string]$AccountLabel = "",
    [string]$Source = "",
    [string]$Endpoint = "",
    [double]$Balance = [double]::NaN,
    [string]$Currency = "",
    [ValidatePattern("^$|^[A-Fa-f0-9]{64}$")]
    [string]$TokenFingerprint = ""
)

$ErrorActionPreference = "Stop"
$pipeName = "BalancePet.Account.v1"

function Assert-SafeValue([string]$Name, [string]$Value, [int]$MaximumLength) {
    if ($Value.Length -gt $MaximumLength -or $Value -match "\p{C}") {
        throw "$Name contains unsupported characters or is too long."
    }
}

Assert-SafeValue "Provider" $Provider 32
Assert-SafeValue "AccountLabel" $AccountLabel 96
Assert-SafeValue "Source" $Source 160
Assert-SafeValue "Endpoint" $Endpoint 160
Assert-SafeValue "Currency" $Currency 16
if (-not [double]::IsNaN($Balance) -and [double]::IsInfinity($Balance)) {
    throw "Balance must be a finite number."
}

# This protocol carries login metadata only. Never pass a plaintext token.
$payload = @{
    state = $State
    provider = $Provider.Trim()
    accountType = $AccountType
    accountLabel = $AccountLabel.Trim()
    source = $Source.Trim()
    endpoint = $Endpoint.Trim()
    tokenFingerprint = $TokenFingerprint.Trim().ToLowerInvariant()
}
if (-not [double]::IsNaN($Balance)) { $payload.balance = $Balance }
if (-not [string]::IsNullOrWhiteSpace($Currency)) { $payload.currency = $Currency.Trim() }
$message = $payload | ConvertTo-Json -Compress

$sent = $false
for ($attempt = 0; $attempt -lt 3 -and -not $sent; $attempt++) {
    $pipe = $null
    $writer = $null
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            $pipeName,
            [System.IO.Pipes.PipeDirection]::Out,
            [System.IO.Pipes.PipeOptions]::Asynchronous
        )
        $pipe.Connect(500)
        $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false))
        $writer.AutoFlush = $true
        $writer.WriteLine($message)
        $sent = $true
    }
    catch {
        if ($attempt -lt 2) { Start-Sleep -Milliseconds 80 }
    }
    finally {
        if ($writer) { $writer.Dispose() }
        if ($pipe) { $pipe.Dispose() }
    }
}

if (-not $sent) {
    [Console]::Error.WriteLine("BalancePet is not running or account status integration is disabled.")
    exit 2
}
exit 0
