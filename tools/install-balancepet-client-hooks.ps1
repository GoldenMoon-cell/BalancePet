[CmdletBinding()]
param(
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

if ($Client -in @("All", "Gemini"))
{
    $settings = Get-SettingsObject $GeminiSettingsPath
    $hooks = Get-OrAddObjectProperty $settings "hooks"
    Set-BalancePetHook $hooks "BeforeAgent" "start" "Gemini"
    Set-BalancePetHook $hooks "AfterAgent" "stop" "Gemini"
    Set-BalancePetHook $hooks "SessionEnd" "stop" "Gemini"
    Save-Settings $GeminiSettingsPath $settings
    Write-Host "Installed BalancePet hooks for Gemini CLI: $GeminiSettingsPath"
}

if ($Client -in @("All", "Qwen"))
{
    $settings = Get-SettingsObject $QwenSettingsPath
    $hooks = Get-OrAddObjectProperty $settings "hooks"
    Set-BalancePetHook $hooks "UserPromptSubmit" "start" "Qwen"
    Set-BalancePetHook $hooks "Stop" "stop" "Qwen"
    Set-BalancePetHook $hooks "StopFailure" "stop" "Qwen" ".*"
    Set-BalancePetHook $hooks "SessionEnd" "stop" "Qwen"
    Save-Settings $QwenSettingsPath $settings
    Write-Host "Installed BalancePet hooks for Qwen Code: $QwenSettingsPath"
}

if ($Client -in @("All", "Claude"))
{
    $settings = Get-SettingsObject $ClaudeSettingsPath
    $hooks = Get-OrAddObjectProperty $settings "hooks"
    Set-BalancePetHook $hooks "UserPromptSubmit" "start" "Claude"
    Set-BalancePetHook $hooks "Stop" "stop" "Claude"
    Set-BalancePetHook $hooks "StopFailure" "stop" "Claude"
    Set-BalancePetHook $hooks "SessionEnd" "stop" "Claude"
    Save-Settings $ClaudeSettingsPath $settings
    Write-Host "Installed BalancePet hooks for Claude Code: $ClaudeSettingsPath"
}
