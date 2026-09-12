using System.Reflection;

namespace BalancePet.Wpf.Services;

/// <summary>
/// The extension managers must validate manifests against the core that is
/// actually running. Keeping this in one place prevents a release bump from
/// leaving a stale hard-coded minimum-version check behind.
/// </summary>
internal static class CoreVersion
{
    private static readonly Version Fallback = new(0, 0, 0);

    public static Version Current
    {
        get
        {
            var assembly = typeof(CoreVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (TryParse(informational, out var version)) return version;

            return assembly.GetName().Version ?? Fallback;
        }
    }

    private static bool TryParse(string? value, out Version version)
    {
        var numeric = value?.Split('-', '+')[0];
        return Version.TryParse(numeric, out version!);
    }
}
