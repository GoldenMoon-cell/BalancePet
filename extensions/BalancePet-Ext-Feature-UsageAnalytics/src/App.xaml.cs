using System.IO;
using System.Windows;

namespace BalancePet.UsageAnalytics;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        Resources["WindowCornerRadius"] = Environment.OSVersion.Version.Build >= 22000 ? new CornerRadius(10) : new CornerRadius(4);
        var dataDirectory = ReadArgument(e.Args, "--data-dir")
            ?? UsageEventStore.GetDefaultDirectory();
        var window = new MainWindow(dataDirectory);
        MainWindow = window;
        window.Show();
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) continue;
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) return null;
            return Path.GetFullPath(args[index + 1]);
        }
        return null;
    }
}
