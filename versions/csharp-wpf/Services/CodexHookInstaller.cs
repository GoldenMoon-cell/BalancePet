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
        var groups = hooks[eventName] as JsonArray ?? new JsonArray();
        hooks[eventName] = groups;
        if (ContainsBalancePetHook(groups)) return;

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
                    ["timeout"] = 3,
                    ["async"] = true
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
            $hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
            $message = @{
                state = $State
                sessionId = [string]$hookInput.session_id
                turnId = [string]$hookInput.turn_id
            } | ConvertTo-Json -Compress

            $sent = $false
            for ($attempt = 0; $attempt -lt 3 -and -not $sent; $attempt++) {
                $pipe = $null
                try {
                    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
                        '.',
                        '{{CodexTaskBridge.PipeName}}',
                        [System.IO.Pipes.PipeDirection]::Out,
                        [System.IO.Pipes.PipeOptions]::Asynchronous
                    )
                    $pipe.Connect(800)
                    $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false))
                    $writer.AutoFlush = $true
                    $writer.WriteLine($message)
                    $writer.Dispose()
                    $sent = $true
                }
                catch {
                    if ($attempt -lt 2) { Start-Sleep -Milliseconds 100 }
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
