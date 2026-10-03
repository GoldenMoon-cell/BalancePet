using System.IO;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Moves an unpacked package out of its staging directory and into place.
/// </summary>
/// <remarks>
/// This exists because <see cref="Directory.Move(string, string)"/> on a directory that
/// was just written into is intermittently refused, with ERROR_ACCESS_DENIED naming the
/// source. Measured here by writing one appearance's nine images and moving the
/// directory straight afterwards: two refusals in sixty. The same move forty
/// milliseconds later was never refused, and neither was moving an empty directory or
/// reading the files back first.
///
/// So the cause is outside this program. A virus scanner or an indexer still holds a
/// handle on a file that was written a moment ago, and Windows will not move a
/// directory while anything has an open handle inside it. The package manager's own
/// install path does more work than that measurement — it builds a ZIP, extracts it,
/// and reads the manifest — and installing an appearance failed 14 times in 60 there,
/// which is a user pressing Install and being told "access denied" for a file they
/// never touched.
///
/// A short wait is therefore the fix, and a copy is the backstop: reading the files is
/// not what gets refused, so if the move will not go through, the tree can still be
/// reproduced and the staging copy abandoned.
/// </remarks>
public static class StagedPackage
{
    /// <summary>
    /// How long to wait before each attempt. The first attempt is immediate, because
    /// the common case is that nothing is holding anything and a delay would be paid
    /// by every install to make a rare one work.
    /// </summary>
    private static readonly int[] RetryDelays = { 0, 40, 120, 300 };

    /// <summary>
    /// Puts the staged tree at <paramref name="destinationRoot"/>, which must not exist.
    /// </summary>
    /// <exception cref="IOException">The tree could not be moved or copied.</exception>
    public static void Commit(string stagingRoot, string destinationRoot)
    {
        Exception? refused = null;
        foreach (var delay in RetryDelays)
        {
            if (delay > 0) Thread.Sleep(delay);
            try
            {
                Directory.Move(stagingRoot, destinationRoot);
                return;
            }
            catch (IOException error) { refused = error; }
            catch (UnauthorizedAccessException error) { refused = error; }
        }

        try
        {
            CopyTree(stagingRoot, destinationRoot);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Leaving a half-copied version behind would be worse than the failure: the
            // version directory is what "installed" means, and a package missing some of
            // its files would be found by the next launch and reported as installed.
            TryDelete(destinationRoot);
            throw refused ?? error;
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
