[CmdletBinding()]
param(
    [ValidateSet("Install", "Remove", "Status")]
    [string]$Action = "Install",

    [ValidateSet("All", "Gemini", "Qwen", "Claude")]
    [string]$Client = "All",

    [string]$HookScriptPath = "",

    [string]$GeminiSettingsPath = (Join-Path $env:USERPROFILE ".gemini\settings.json"),

    [string]$QwenSettingsPath = (Join-Path $env:USERPROFILE ".qwen\settings.json"),

    [string]$ClaudeSettingsPath = (Join-Path $env:USERPROFILE ".claude\settings.json")
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($HookScriptPath))
{
    $HookScriptPath = Join-Path $PSScriptRoot "balancepet-client-hook.ps1"
}
$HookScriptPath = [System.IO.Path]::GetFullPath($HookScriptPath)
if (-not (Test-Path -LiteralPath $HookScriptPath -PathType Leaf))
{
    throw "BalancePet client hook was not found: $HookScriptPath"
}

function Get-SettingsObject([string]$Path)
{
    if (-not (Test-Path -LiteralPath $Path)) { return [pscustomobject]@{} }
    $text = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($text)) { return [pscustomobject]@{} }
    $settings = $text | ConvertFrom-Json
    if ($null -eq $settings -or $settings -isnot [pscustomobject])
    {
        throw "Settings root must be a JSON object: $Path"
    }
    return $settings
}

function Get-OrAddObjectProperty([pscustomobject]$Object, [string]$Name)
{
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property)
    {
        $value = [pscustomobject]@{}
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $value
        return $value
    }
    if ($null -eq $property.Value)
    {
        $property.Value = [pscustomobject]@{}
        return $property.Value
    }
    if ($property.Value -isnot [pscustomobject])
    {
        throw "Settings property '$Name' must be a JSON object."
    }
    return $property.Value
}

function Set-BalancePetHook(
    [pscustomobject]$Hooks,
    [string]$Event,
    [string]$State,
    [string]$Provider,
    [string]$Matcher = ""
)
{
    $name = "BalancePet-$Provider-$Event"
    $timeout = if ($Provider -eq "Claude") { 5 } else { 5000 }
    $handler = [ordered]@{
        type = "command"
        name = $name
        timeout = $timeout
        description = "Report $Provider task state to the local BalancePet app"
    }
    if ($Provider -eq "Claude")
    {
        # Claude Code's Windows hook format keeps the executable and arguments
        # separate, avoiding shell quoting differences between cmd.exe and Git Bash.
        $handler.command = "powershell.exe"
        $handler.args = @(
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            $HookScriptPath,
            "-State",
            $State,
            "-Provider",
            $Provider
        )
    }
    else
    {
        $handler.command = 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -State {1} -Provider "{2}"' -f $HookScriptPath, $State, $Provider
        if ($Provider -eq "Qwen") { $handler.shell = "powershell" }
    }
    $definition = [ordered]@{ hooks = @([pscustomobject]$handler) }
    if (-not [string]::IsNullOrWhiteSpace($Matcher)) { $definition.matcher = $Matcher }

    $property = $Hooks.PSObject.Properties[$Event]
    $existing = if ($null -eq $property) { @() } else { @($property.Value) }
    $preserved = @($existing | Where-Object {
        $names = @($_.hooks | ForEach-Object { $_.name })
        $names -notcontains $name
    })
    $updated = @($preserved) + @([pscustomobject]$definition)
    if ($null -eq $property) { $Hooks | Add-Member -NotePropertyName $Event -NotePropertyValue $updated }
    else { $property.Value = $updated }
}

function Remove-BalancePetHook(
    [pscustomobject]$Hooks,
    [string]$Event,
    [string]$Provider
)
{
    $name = "BalancePet-$Provider-$Event"
    $property = $Hooks.PSObject.Properties[$Event]
    if ($null -eq $property) { return }
    $kept = @($property.Value | Where-Object {
        $names = @($_.hooks | ForEach-Object { $_.name })
        $names -notcontains $name
    })
    # Drop the event key entirely once nothing is left, so an uninstalled client
    # leaves no empty scaffolding behind.
    if ($kept.Count -eq 0) { $Hooks.PSObject.Properties.Remove($Event) }
    else { $property.Value = $kept }
}

function Test-BalancePetHook(
    [pscustomobject]$Hooks,
    [string]$Event,
    [string]$Provider
)
{
    $name = "BalancePet-$Provider-$Event"
    $property = $Hooks.PSObject.Properties[$Event]
    if ($null -eq $property) { return $false }
    foreach ($entry in @($property.Value))
    {
        $names = @($entry.hooks | ForEach-Object { $_.name })
        if ($names -contains $name) { return $true }
    }
    return $false
}

function Save-Settings([string]$Path, [pscustomobject]$Settings)
{
    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if (-not [string]::IsNullOrWhiteSpace($directory))
    {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    if (Test-Path -LiteralPath $Path)
    {
        $stamp = Get-Date -Format "yyyyMMddHHmmss"
        Copy-Item -LiteralPath $Path -Destination "$Path.balancepet-backup-$stamp" -Force
    }
    $temporary = "$Path.balancepet-tmp"
    $json = $Settings | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($temporary, $json, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

# Each client's hook layout as data, so Install, Remove and Status share one
# definition instead of drifting apart.
$clientSpecs = @(
    [pscustomobject]@{
        Client = "Gemini"; Label = "Gemini CLI"; Path = $GeminiSettingsPath
        Events = @(
            [pscustomobject]@{ Event = "BeforeAgent"; State = "start"; Matcher = "" }
            [pscustomobject]@{ Event = "AfterAgent"; State = "stop"; Matcher = "" }
            [pscustomobject]@{ Event = "SessionEnd"; State = "stop"; Matcher = "" }
        )
    }
    [pscustomobject]@{
        Client = "Qwen"; Label = "Qwen Code"; Path = $QwenSettingsPath
        Events = @(
            [pscustomobject]@{ Event = "UserPromptSubmit"; State = "start"; Matcher = "" }
            [pscustomobject]@{ Event = "Stop"; State = "stop"; Matcher = "" }
            [pscustomobject]@{ Event = "StopFailure"; State = "stop"; Matcher = ".*" }
            [pscustomobject]@{ Event = "SessionEnd"; State = "stop"; Matcher = "" }
        )
    }
    [pscustomobject]@{
        Client = "Claude"; Label = "Claude Code"; Path = $ClaudeSettingsPath
        Events = @(
            [pscustomobject]@{ Event = "UserPromptSubmit"; State = "start"; Matcher = "" }
            [pscustomobject]@{ Event = "Stop"; State = "stop"; Matcher = "" }
            [pscustomobject]@{ Event = "StopFailure"; State = "stop"; Matcher = "" }
            [pscustomobject]@{ Event = "SessionEnd"; State = "stop"; Matcher = "" }
        )
    }
)

foreach ($spec in $clientSpecs)
{
    if ($Client -ne "All" -and $Client -ne $spec.Client) { continue }

    $directory = [System.IO.Path]::GetDirectoryName($spec.Path)

    if ($Action -eq "Status")
    {
        if (-not (Test-Path -LiteralPath $spec.Path))
        {
            Write-Host "$($spec.Label): not configured ($($spec.Path))"
            continue
        }
        $settings = Get-SettingsObject $spec.Path
        $hooks = Get-OrAddObjectProperty $settings "hooks"
        $installed = 0
        foreach ($item in $spec.Events)
        {
            if (Test-BalancePetHook $hooks $item.Event $spec.Client) { $installed++ }
        }
        Write-Host "$($spec.Label): $installed of $($spec.Events.Count) hooks installed ($($spec.Path))"
        continue
    }

    # Skip a client that is neither configured nor installed, so this script
    # never creates a settings file for software the user does not have.
    if (-not (Test-Path -LiteralPath $spec.Path) -and -not (Test-Path -LiteralPath $directory))
    {
        Write-Host "$($spec.Label): not present, skipped ($directory)"
        continue
    }

    $settings = Get-SettingsObject $spec.Path
    $hooks = Get-OrAddObjectProperty $settings "hooks"
    foreach ($item in $spec.Events)
    {
        if ($Action -eq "Install") { Set-BalancePetHook $hooks $item.Event $item.State $spec.Client $item.Matcher }
        else { Remove-BalancePetHook $hooks $item.Event $spec.Client }
    }
    Save-Settings $spec.Path $settings
    if ($Action -eq "Install") { Write-Host "Installed BalancePet hooks for $($spec.Label): $($spec.Path)" }
    else { Write-Host "Removed BalancePet hooks for $($spec.Label): $($spec.Path)" }
}
