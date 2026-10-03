using System.IO;

namespace BalancePet.Wpf.Services;

/// <summary>
/// Where an unhandled exception is written down instead of taking the pet with it.
/// </summary>
/// <remarks>
/// The program had nowhere to write one until two unrelated faults each ended the process:
/// a font family whose file is missing threw while the settings window was building its
/// font list, and a catalog load used an HTTP client the window had already disposed.
/// Neither has anything to do with the pet or with polling, and neither is a reason to
/// lose it. Windows records both — in the event log, which is not where a user looks.
/// </remarks>
public static class CrashLog
{
    /// <summary>
    /// Appended to, and started over once it grows past this: a record of failures must not
    /// become one. A fault that repeats on a timer would otherwise fill a disk.
    /// </summary>
    private const long MaxBytes = 512 * 1024;

    /// <summary>
    /// Where the record goes. Redirectable through the environment so it can be pointed at
    /// a scratch file — by the self-test, and by anyone diagnosing a machine without
    /// wanting to go looking in the profile directory.
    /// </summary>
    public static string FilePath
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("BALANCEPET_CRASH_LOG");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BalancePet", "crash.log")
                : custom;
        }
    }

    /// <summary>The name shown to a user, who does not need the whole path in a bubble.</summary>
    public static string FileName => "crash.log";

    public static void Write(string origin, Exception error)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
            File.AppendAllText(
                FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origin}{Environment.NewLine}"
                + $"{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Being unable to write down a failure must not become one.
        }
    }
}
