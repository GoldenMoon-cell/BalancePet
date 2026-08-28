using System.IO;
using System.Security;
using Microsoft.Win32;

namespace BalancePet.Wpf.Services;

public static class StartupManager
{
    private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "BalancePetCSharp";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName)?.ToString());
        }
        catch (SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) return false;
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                key?.SetValue(ValueName, $"\"{executable}\"");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return IsEnabled() == enabled;
        }
        catch (SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }
}
