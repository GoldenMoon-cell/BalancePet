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
$offset = 0
while ($offset -lt $buffer.Length)
{
    $read = $reader.Read($buffer, $offset, $buffer.Length - $offset)
    if ($read -le 0) { break }
    $offset += $read
}
$inputText = if ($offset -gt 0) { -join $buffer[0..($offset - 1)] } else { "" }

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

$message = @{
    state = $State
    sessionId = "external:$Provider"
    turnId = $taskId
    provider = $Provider
} | ConvertTo-Json -Compress

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
