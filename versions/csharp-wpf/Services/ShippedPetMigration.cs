using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Turns pet appearances that shipped inside the application folder into installed
/// extension packages.
/// </summary>
/// <remarks>
/// An upgraded installation keeps the previous release's pet folders, because an
/// installer only adds and replaces files. Those folders still draw correctly, but
/// the extension list does not know about them: the user cannot remove them, they
/// never receive an update, and they still occupy the installation directory. This
/// converts each one into a real package so that all of that becomes possible.
///
/// The order matters. The package is installed and verified first, and only then is
/// the original directory removed. If anything fails, the appearance is left exactly
/// as it was — still drawing as a shipped pet — so the worst case is that the
/// migration runs again on the next launch rather than that a pet disappears.
/// </remarks>
public static class ShippedPetMigration
{
    /// <summary>State images a package must contain to be worth installing.</summary>
    private static readonly string[] RequiredStates =
    [
        "idle.png", "loading.png", "success.png", "low.png", "error.png",
        "clicked.png", "codex-working.png", "codex-done.png", "inactive.png"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>
    /// Outcome of one migration pass.
    /// </summary>
    /// <param name="Migrated">Appearances converted into packages.</param>
    /// <param name="Reclaimed">Appearances removed because a package already covered them.</param>
    /// <param name="Failed">Appearances left untouched, with the reason.</param>
    public sealed record Result(int Migrated, int Reclaimed, IReadOnlyList<string> Failed)
    {
        public bool ChangedAnything => Migrated > 0 || Reclaimed > 0;
        public static Result Nothing => new(0, 0, Array.Empty<string>());
    }

    /// <summary>
    /// Converts every eligible appearance. Safe to call on each launch: once the
    /// shipping folders are gone there is nothing left to find, and a half-finished
    /// run simply resumes.
    /// </summary>
    public static Result Run(string? applicationDirectory = null, string? extensionsRoot = null)
    {
        var root = applicationDirectory ?? AppContext.BaseDirectory;
        var petsRoot = Path.Combine(root, "assets", "pets");
        if (!Directory.Exists(petsRoot)) return Result.Nothing;

        PetExtensionManager manager;
        try
        {
            manager = extensionsRoot is null ? new PetExtensionManager() : new PetExtensionManager(extensionsRoot);
        }
        catch (IOException) { return Result.Nothing; }

        // An appearance that is already installed under the same style needs no
        // package built; the shipping folder is then only a stale duplicate.
        HashSet<string> packaged;
        try
        {
            packaged = manager.GetInstalled()
                .Select(info => info.StyleId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException) { packaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

        var migrated = 0;
        var reclaimed = 0;
        var failed = new List<string>();

        foreach (var directory in Directory.EnumerateDirectories(petsRoot).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var style = Path.GetFileName(directory);
            if (style.StartsWith('_') || style.StartsWith('.')) continue;

            // Only convert ids the catalogue actually knows. An unrecognised folder
            // is somebody else's business and is left alone rather than guessed at.
            if (!PetStyleCatalog.TryGetDefinition(style, out var definition)) continue;
            if (!string.Equals(definition.Id, style, StringComparison.OrdinalIgnoreCase)) continue;
            if (RequiredStates.Any(state => !File.Exists(Path.Combine(directory, state)))) continue;

            if (packaged.Contains(style))
            {
                // The package is the managed copy and the one the user can act on.
                if (TryRemoveDirectory(directory)) reclaimed++;
                continue;
            }

            try
            {
                InstallFrom(directory, definition, manager);
                if (!TryRemoveDirectory(directory)) { failed.Add($"{style}：已安装，但原目录无法删除。"); continue; }
                packaged.Add(style);
                migrated++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException)
            {
                // Left in place on purpose: it still draws, so a failure costs the
                // user nothing but a retry on the next launch.
                failed.Add($"{style}：{error.Message}");
            }
        }

        return new Result(migrated, reclaimed, failed);
    }

    private static void InstallFrom(string sourceDirectory, PetStyleDefinition definition, PetExtensionManager manager)
    {
        // The manager installs from a ZIP, so the appearance is staged into one. The
        // staging copy is what makes the original disposable: nothing is written
        // inside the installation directory while the package is being built.
        var staging = Path.Combine(Path.GetTempPath(), $"BalancePet-pet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var assets = Path.Combine(staging, "assets", "pets", definition.Id);
            Directory.CreateDirectory(assets);
            foreach (var state in RequiredStates) File.Copy(Path.Combine(sourceDirectory, state), Path.Combine(assets, state), overwrite: true);

            // `style` is deliberately the built-in id rather than the package id: a
            // saved setting stores that id, so any other value would stop resolving
            // for the very users this migration exists for.
            var manifest = new
            {
                id = $"pet.{definition.Id}",
                type = "pet",
                name = definition.ChineseName,
                name_en = definition.EnglishName,
                style = definition.Id,
                version = "1.0.0",
                api_version = 1,
                min_core_version = "0.5.0"
            };
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            var archive = Path.Combine(Path.GetTempPath(), $"BalancePet-pet-{Guid.NewGuid():N}.zip");
            try
            {
                ZipFile.CreateFromDirectory(staging, archive, CompressionLevel.Optimal, includeBaseDirectory: false);
                manager.InstallPetPackage(archive);
            }
            finally
            {
                try { File.Delete(archive); } catch (IOException) { }
            }
        }
        finally
        {
            TryRemoveDirectory(staging);
        }
    }

    private static bool TryRemoveDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
