[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("start", "stop")]
    [string]$State,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[A-Za-z0-9 ._-]{1,32}$")]
    [string]$Provider
)

# Gemini CLI and Qwen Code pass their hook payload on standard input. Read only
# a bounded prefix and extract the session ID; prompts, replies, and credentials
# are never parsed, persisted, or sent to BalancePet.
$pipeName = "BalancePet.Task.v1"
$inputStream = [Console]::OpenStandardInput()
$buffer = New-Object char[] 8192
$reader = [System.IO.StreamReader]::new($inputStream, [System.Text.UTF8Encoding]::new($false), $true, 8192, $false)
# Read a bounded prefix with a deadline. When a client is interrupted, stdin
# may never close; a hook must still be able to send the Stop event.
$readTask = $reader.ReadAsync($buffer, 0, $buffer.Length)
$readCount = if ($readTask.Wait(1000)) { [int]$readTask.Result } else { 0 }
$inputText = if ($readCount -gt 0) { -join $buffer[0..($readCount - 1)] } else { "" }
$hookInput = $null
if (-not [string]::IsNullOrWhiteSpace($inputText)) {
    try { $hookInput = $inputText | ConvertFrom-Json } catch { $hookInput = $null }
}

$sessionMatch = [regex]::Match(
    $inputText,
    '"(?:session_id|sessionId|session)"\s*:\s*"(?<id>[A-Za-z0-9._:-]{1,256})"',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)

if ($sessionMatch.Success)
{
    $taskId = $sessionMatch.Groups["id"].Value
}
else
{
    # Older clients may omit the common session_id field. Keep a stable,
    # metadata-only identity so a short turn can still be shown and closed.
    $taskId = "hook"
}

$message = [ordered]@{
    state = $State
    sessionId = "external:$Provider"
    turnId = $taskId
    provider = $Provider
}
$usageSources = if ($null -ne $hookInput) {
    @(
        $hookInput.usage,
        $hookInput.token_usage,
        $hookInput.usage_metadata,
        $hookInput.usageMetadata,
        $hookInput.tokenUsage,
        $hookInput.response.usage,
        $hookInput.response.usage_metadata,
        $hookInput.response.usageMetadata,
        $hookInput.result.usage,
        $hookInput.result.usage_metadata,
        $hookInput.metadata,
        $hookInput.stats,
        $hookInput.metrics,
        $hookInput
    )
} else { @($null) }
$usageAliases = @{
    model = @('model')
    input_tokens = @('input_tokens', 'inputTokens', 'prompt_tokens', 'promptTokens', 'input_token_count', 'inputTokenCount', 'prompt_token_count', 'promptTokenCount')
    output_tokens = @('output_tokens', 'outputTokens', 'completion_tokens', 'completionTokens', 'output_token_count', 'outputTokenCount', 'candidates_token_count', 'candidatesTokenCount', 'completion_token_count', 'completionTokenCount')
    cache_read_tokens = @('cache_read_tokens', 'cacheReadTokens', 'cached_tokens', 'cachedTokens', 'cache_read_input_tokens', 'cacheReadInputTokens', 'cache_hit_tokens', 'cacheHitTokens')
    cache_write_tokens = @('cache_write_tokens', 'cacheWriteTokens', 'cache_creation_input_tokens', 'cacheCreationInputTokens')
    duration_ms = @('duration_ms', 'durationMs', 'elapsed_ms', 'elapsedMs')
    time_to_first_token_ms = @('time_to_first_token_ms', 'timeToFirstTokenMs', 'ttft_ms', 'time_to_first_token', 'timeToFirstToken')
    tool_calls = @('tool_calls', 'toolCalls')
    steps = @('steps')
    success = @('success')
}
foreach ($field in $usageAliases.Keys) {
    $value = $null
    foreach ($usageSource in $usageSources) { if ($null -ne $usageSource) { foreach ($candidate in $usageAliases[$field]) { if ($null -ne $usageSource.$candidate) { $value = $usageSource.$candidate; break } } } if ($null -ne $value) { break } }
    if ($null -eq $value) { continue }
    if ($field -eq 'model') {
        if (-not [string]::IsNullOrWhiteSpace([string]$value) -and ([string]$value).Length -le 160 -and ([string]$value) -notmatch "\p{C}") { $message[$field] = [string]$value }
    }
    elseif ($value -is [int] -or $value -is [long] -or $value -is [double] -or $value -is [decimal]) {
        $number = [long]$value
        if ($number -ge 0 -and $number -le 10000000000) { $message[$field] = $number }
    }
    elseif ($field -eq 'success' -and $value -is [bool]) { $message[$field] = $value }
}
$message = $message | ConvertTo-Json -Compress

# A hook must never interrupt its client. The pipe is local to the current
# Windows user, and a short best-effort send is enough for the desktop app.
for ($attempt = 0; $attempt -lt 2; $attempt++)
{
    $pipe = $null
    $writer = $null
    try
    {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            ".",
            $pipeName,
            [System.IO.Pipes.PipeDirection]::Out,
            [System.IO.Pipes.PipeOptions]::Asynchronous
        )
        $pipe.Connect(350)
        $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false))
        $writer.AutoFlush = $true
        $writer.WriteLine($message)
        break
    }
    catch
    {
        if ($attempt -eq 0) { Start-Sleep -Milliseconds 60 }
    }
    finally
    {
        if ($writer) { $writer.Dispose() }
        if ($pipe) { $pipe.Dispose() }
    }
}

# Command hooks reserve stdout for a JSON response. This output is advisory and
# contains no task data, so it neither modifies nor exposes the AI session.
[Console]::Out.WriteLine("{}")
exit 0
