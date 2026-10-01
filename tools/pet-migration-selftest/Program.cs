using System.IO;
using System.IO.Compression;
using System.Text.Json;
using BalancePet.Wpf.Services;

namespace BalancePet.SelfTest;

/// <summary>
/// Exercises <see cref="ShippedPetMigration"/> against throwaway directories.
///
/// The migration is destructive by design — it deletes the folders it converts —
/// and it has to be trusted before it is allowed near a real installation. Running
/// it here proves the parts that matter: that a package is actually installed, that
/// the original is only removed once that succeeded, that a failure leaves the
/// original untouched, and that running twice changes nothing the second time.
/// </summary>
internal static class Program
{
    private static readonly string[] RequiredStates =
    [
        "idle.png", "loading.png", "success.png", "low.png", "error.png",
        "clicked.png", "codex-working.png", "codex-done.png", "inactive.png"
    ];

    private static int _failures;

    private static int Main()
    {
        // Deliberately not under %TEMP%. The workspace carries a low mandatory
        // label, so anything started from it inherits a low integrity token and is
        // refused when it tries to create a directory in a medium-integrity
        // location. The build output directory is writable, so scratch space lives
        // beside the test binary instead.
        var workspace = Path.Combine(AppContext.BaseDirectory, $"selftest-{Guid.NewGuid():N}");
        var appDirectory = Path.Combine(workspace, "app");
        var extensionsRoot = Path.Combine(workspace, "extensions");
        var realPets = LocateRealPetAssets();

        if (realPets is null)
        {
            Console.WriteLine("找不到真实形象素材，无法运行自测。");
            return 2;
        }

        try
        {
            // --- 1. Two shipped appearances become packages -------------------
            Stage(appDirectory, realPets, "qwen");
            Stage(appDirectory, realPets, "gemini");
            var result = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("迁移数量 = 2", result.Migrated == 2, $"实际 {result.Migrated}");
            Check("无失败", result.Failed.Count == 0, string.Join("；", result.Failed));
            Check("qwen 原目录已删除", !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "qwen")));
            Check("gemini 原目录已删除", !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "gemini")));
            Check("qwen 包已安装", InstalledStyles(extensionsRoot).Contains("qwen"));
            Check("gemini 包已安装", InstalledStyles(extensionsRoot).Contains("gemini"));
            Check("包的 style 等于内置 id", ManifestStyle(extensionsRoot, "pet.qwen") == "qwen",
                $"实际 {ManifestStyle(extensionsRoot, "pet.qwen")}");
            Check("包内含 9 张状态图", CountStates(extensionsRoot, "pet.qwen") == 9,
                $"实际 {CountStates(extensionsRoot, "pet.qwen")}");

            // --- 2. A second run is a no-op -----------------------------------
            var again = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("重复运行不迁移", again.Migrated == 0, $"实际 {again.Migrated}");
            Check("重复运行不失败", again.Failed.Count == 0, string.Join("；", again.Failed));

            // --- 3. Incomplete artwork is left alone --------------------------
            Stage(appDirectory, realPets, "claude");
            File.Delete(Path.Combine(appDirectory, "assets", "pets", "claude", "idle.png"));
            var partial = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("缺状态图的不迁移", partial.Migrated == 0, $"实际 {partial.Migrated}");
            Check("缺状态图的原目录保留", Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "claude")));

            // --- 4. An unregistered folder is not guessed at ------------------
            var stranger = Path.Combine(appDirectory, "assets", "pets", "notarealpet");
            Directory.CreateDirectory(stranger);
            foreach (var state in RequiredStates) File.Copy(Path.Combine(realPets, "qwen", state), Path.Combine(stranger, state));
            var unknown = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("未登记的形象不动", unknown.Migrated == 0 && Directory.Exists(stranger), $"迁移 {unknown.Migrated}");

            // --- 5. A duplicate of an installed package is only reclaimed -----
            Stage(appDirectory, realPets, "qwen");
            var duplicate = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("重复形象被回收", duplicate.Reclaimed == 1, $"实际 {duplicate.Reclaimed}");
            Check("重复形象不再迁移", duplicate.Migrated == 0, $"实际 {duplicate.Migrated}");
            Check("重复形象目录已删除", !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "qwen")));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : $"\n{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok, string detail = "")
    {
        if (ok) { Console.WriteLine($"  PASS  {what}"); return; }
        _failures++;
        Console.WriteLine($"  FAIL  {what}{(detail.Length == 0 ? "" : $"  ({detail})")}");
    }

    /// <summary>Copies a real appearance into a throwaway application directory.</summary>
    private static void Stage(string appDirectory, string realPets, string style)
    {
        var target = Path.Combine(appDirectory, "assets", "pets", style);
        Directory.CreateDirectory(target);
        foreach (var state in RequiredStates) File.Copy(Path.Combine(realPets, style, state), Path.Combine(target, state), overwrite: true);
    }

    /// <summary>
    /// Locates the repository's real artwork so the packages built here contain
    /// genuine images rather than fixtures that could pass with the wrong paths.
    /// </summary>
    private static string? LocateRealPetAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "versions", "csharp-wpf", "assets", "pets");
            if (Directory.Exists(Path.Combine(candidate, "qwen")) && Directory.Exists(Path.Combine(candidate, "gemini"))) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    private static IReadOnlyList<string> InstalledStyles(string extensionsRoot)
    {
        var manager = new PetExtensionManager(extensionsRoot);
        return manager.GetInstalled().Select(info => info.StyleId).ToArray();
    }

    private static string ManifestStyle(string extensionsRoot, string packageId)
    {
        var packageRoot = Path.Combine(extensionsRoot, packageId);
        if (!Directory.Exists(packageRoot)) return "";
        var manifest = Directory.EnumerateFiles(packageRoot, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
        if (manifest is null) return "";
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        return document.RootElement.TryGetProperty("style", out var style) ? style.GetString() ?? "" : "";
    }

    private static int CountStates(string extensionsRoot, string packageId)
        => Directory.Exists(Path.Combine(extensionsRoot, packageId))
            ? Directory.EnumerateFiles(Path.Combine(extensionsRoot, packageId), "*.png", SearchOption.AllDirectories).Count()
            : 0;
}
