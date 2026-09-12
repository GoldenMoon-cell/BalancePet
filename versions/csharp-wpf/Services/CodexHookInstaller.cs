using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BalancePet.Wpf.Services;

public static class CodexHookInstaller
{
    private const string ScriptFileName = "balancepet-codex-task-hook.ps1";
    private const string HookMarker = "BalancePetCodexTaskHook";

    private static string CodexDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private static string HooksPath => Path.Combine(CodexDirectory, "hooks.json");
    private static string ScriptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalancePet", ScriptFileName);

    public static bool IsInstalled()
    {
        try
        {
            if (!File.Exists(HooksPath) || !File.Exists(ScriptPath)) return false;
            return File.ReadAllText(HooksPath).Contains(HookMarker, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool TryInstall(out string error)
    {
        error = "";
        try
        {
            Directory.CreateDirectory(CodexDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
            File.WriteAllText(ScriptPath, BuildBridgeScript(), new UTF8Encoding(true));

            var root = LoadHooksRoot();
            var hooks = root["hooks"] as JsonObject ?? new JsonObject();
            root["hooks"] = hooks;
            AddHook(hooks, "UserPromptSubmit", "start");
            AddHook(hooks, "Stop", "stop");
            WriteHooks(root);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool TryUninstall(out string error)
    {
        error = "";
        try
        {
            if (File.Exists(HooksPath))
            {
                var root = LoadHooksRoot();
                if (root["hooks"] is JsonObject hooks)
                {
                    RemoveHook(hooks, "UserPromptSubmit");
                    RemoveHook(hooks, "Stop");
                }
                WriteHooks(root);
            }
            if (File.Exists(ScriptPath)) File.Delete(ScriptPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static JsonObject LoadHooksRoot()
    {
        if (!File.Exists(HooksPath)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(HooksPath)) as JsonObject
            ?? throw new JsonException("Codex hooks.json 的根节点必须是 JSON 对象。");
    }

    private static void AddHook(JsonObject hooks, string eventName, string state)
    {
        // Always replace our own entry so saving settings upgrades hook options
        // as well as the bridge script, while leaving other hook entries intact.
        RemoveHook(hooks, eventName);
        var groups = hooks[eventName] as JsonArray ?? new JsonArray();
        hooks[eventName] = groups;

        var command = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{ScriptPath}\" {state} {HookMarker}";
        groups.Add(new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                    ["commandWindows"] = command,
                    ["timeout"] = state == "stop" ? 2 : 3,
                    // Codex is about to tear down after Stop. Keep this one
                    // synchronous so the completion event reaches BalancePet.
                    ["async"] = state != "stop"
                }
            }
        });
    }

    private static void RemoveHook(JsonObject hooks, string eventName)
    {
        if (hooks[eventName] is not JsonArray groups) return;
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            if (groups[index] is JsonObject group && ContainsBalancePetHook(group["hooks"] as JsonArray))
                groups.RemoveAt(index);
        }
        if (groups.Count == 0) hooks.Remove(eventName);
    }

    private static bool ContainsBalancePetHook(JsonArray? groups)
    {
        if (groups is null) return false;
        foreach (var item in groups)
        {
            if (item is JsonObject group && ContainsBalancePetHook(group["hooks"] as JsonArray)) return true;
            if (item is JsonObject hook && hook["command"]?.ToJsonString().Contains(HookMarker, StringComparison.Ordinal) == true) return true;
        }
        return false;
    }

    private static void WriteHooks(JsonObject root)
    {
        var temporary = HooksPath + ".balancepet.tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, HooksPath, true);
    }

    private static string BuildBridgeScript() => $$"""
        param(
            [ValidateSet('start', 'stop')][string]$State,
            [string]$Marker
        )

        try {
            $sessionId = ''
            $turnId = ''
            $hookInput = $null
            # Read only a bounded prefix and do not wait forever when a client
            # is interrupted before it closes Hook stdin.
            $inputBuffer = New-Object char[] 65536
            $readTask = [Console]::In.ReadAsync($inputBuffer, 0, $inputBuffer.Length)
            $readCount = if ($readTask.Wait(1000)) { [int]$readTask.Result } else { 0 }
            $inputText = if ($readCount -gt 0) { -join $inputBuffer[0..($readCount - 1)] } else { '' }
            if (-not [string]::IsNullOrWhiteSpace($inputText)) {
                try {
                    $hookInput = $inputText | ConvertFrom-Json
                    $sessionId = [string]$hookInput.session_id
                    $turnId = [string]$hookInput.turn_id
                }
                catch {
                    # Start events require their payload. Stop still needs to
                    # reach BalancePet when a host omits or changes it.
                    if ($State -eq 'start') { throw }
                }
            }
            function Get-UsageValue([string]$Name) {
                $aliases = @{
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
                $names = if ($aliases.ContainsKey($Name)) { $aliases[$Name] } else { @($Name) }
                $usageSources = @(
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
                foreach ($source in $usageSources) {
                    if ($null -eq $source) { continue }
                    foreach ($candidate in $names) { if ($null -ne $source.$candidate) { return $source.$candidate } }
                }
                return $null
            }
            $message = @{
                state = $State
                sessionId = $sessionId
                turnId = $turnId
                provider = 'Codex'
            } | ConvertTo-Json -Compress
            $usageFields = @('model', 'input_tokens', 'output_tokens', 'cache_read_tokens', 'cache_write_tokens', 'duration_ms', 'time_to_first_token_ms', 'tool_calls', 'steps', 'success')
            $messageObject = $message | ConvertFrom-Json
            foreach ($field in $usageFields) {
                $value = Get-UsageValue $field
                if ($null -ne $value -and "$value" -ne '') { $messageObject | Add-Member -Force -NotePropertyName $field -NotePropertyValue $value }
            }
            $message = $messageObject | ConvertTo-Json -Compress

            $maxAttempts = if ($State -eq 'stop') { 2 } else { 3 }
            $connectTimeout = if ($State -eq 'stop') { 300 } else { 800 }
            $sent = $false
            for ($attempt = 0; $attempt -lt $maxAttempts -and -not $sent; $attempt++) {
                $pipe = $null
                try {
                    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
                        '.',
                        '{{CodexTaskBridge.PipeName}}',
                        [System.IO.Pipes.PipeDirection]::Out,
                        [System.IO.Pipes.PipeOptions]::Asynchronous
                    )
                    $pipe.Connect($connectTimeout)
                    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false))
                    $writer.AutoFlush = $true
                    $writer.WriteLine($message)
                    $writer.Dispose()
                    $sent = $true
                }
                catch {
                    if ($attempt -lt ($maxAttempts - 1)) { Start-Sleep -Milliseconds 80 }
                }
                finally {
                    if ($pipe) { $pipe.Dispose() }
                }
            }
        }
        catch {
            # BalancePet may be closed; hooks must never interrupt Codex.
        }

        [Console]::Out.Write('{}')
        """;
}
