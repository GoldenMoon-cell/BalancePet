using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using BalancePet.Wpf.Models;
using MediaColor = System.Windows.Media.Color;

namespace BalancePet.Wpf.Services;

public static class WindowThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    public static void ApplyConfiguredTheme(Window window, PetSettings settings)
    {
        var themes = new ThemeExtensionManager();
        themes.EnsureBundledThemeInstalled();
        var theme = themes.GetLatestEnabled(settings.ThemeId)
            ?? themes.GetLatestEnabled(ThemeExtensionManager.BundledThemeId)
            ?? themes.GetLatestEnabled().FirstOrDefault();
        if (theme is null) return;
        ApplyResources(window, theme, settings.ThemeMode);
        window.SourceInitialized += (_, _) => ApplyBackdropOrFallback(window, settings.ThemeBackdrop, settings.ThemeMode);
    }

    /// <summary>The face embedded in the assembly, used when nothing else is chosen.</summary>
    public const string BundledFontFamily = "pack://application:,,,/assets/fonts/#霞鸜新晰黑, Microsoft YaHei UI";

    /// <summary>
    /// Points the window at a face. Nothing else changes: every control binds UiFontFamily
    /// as a DynamicResource, so replacing the one entry restyles the whole window.
    /// </summary>
    /// <remarks>
    /// A name that no longer resolves falls back to the bundled face rather than throwing.
    /// Fonts get uninstalled, and a settings file is not the place to discover that.
    /// </remarks>
    public static void ApplyFont(Window window, string? font)
    {
        var wanted = string.IsNullOrWhiteSpace(font) ? BundledFontFamily : font.Trim();
        if (window.Resources["UiFontFamily"] is System.Windows.Media.FontFamily current
            && string.Equals(current.Source, wanted, StringComparison.OrdinalIgnoreCase)) return;
        try { window.Resources["UiFontFamily"] = new System.Windows.Media.FontFamily(wanted); }
        catch (Exception error) when (error is ArgumentException or UriFormatException)
        {
            window.Resources["UiFontFamily"] = new System.Windows.Media.FontFamily(BundledFontFamily);
        }
    }

    public static bool ResolveDarkMode(string mode)
    {
        if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { return false; }
    }

    public static void ApplyResources(Window window, ThemeExtensionInfo theme, string mode)
    {
        var dark = ResolveDarkMode(mode);
        var palette = dark ? theme.Theme.Dark : theme.Theme.Light;
        SetBrush(window, "WindowBackgroundBrush", palette.Window);
        SetBrush(window, "SidebarBrush", palette.Sidebar);
        SetBrush(window, "SurfaceBrush", palette.Surface);
        SetBrush(window, "SurfaceStrongBrush", palette.SurfaceStrong);
        SetBrush(window, "ControlBrush", palette.Control);
        SetBrush(window, "TextBrush", palette.Text);
        SetBrush(window, "MutedBrush", palette.Muted);
        SetBrush(window, "BorderBrush", palette.Border);
        SetBrush(window, "AccentBrush", palette.Accent);
        SetBrush(window, "AccentSoftBrush", palette.AccentSoft);
        SetBrush(window, "DangerBrush", palette.Danger);
        SetBrush(window, "WarningSurfaceBrush", palette.WarningSurface);
        SetBrush(window, "WarningTextBrush", palette.WarningText);
        window.Resources["ThemeCornerRadius"] = new CornerRadius(theme.Theme.CornerRadius);
        // One control height, not a compact and a regular one. There was a 紧凑布局 switch
        // that chose between 28 and 32; it changed the height of the two ComboBoxes on the
        // page and nothing else, while promising "show more settings in the same window".
        // The spacing half of it was written by this method and read by nothing at all.
        window.Resources["ThemeControlHeight"] = 32d;
    }

    /// <summary>
    /// Applies one of the two materials the user can choose, or makes the window genuinely
    /// opaque.
    /// </summary>
    /// <remarks>
    /// There used to be four choices. Only one of them worked: Mica Alt and Acrylic were
    /// both routed through <see cref="ApplyBackdropOverlay"/>, which raises the content to
    /// 95-99% opacity to stop those two compositor surfaces going too dark — and at that
    /// opacity the difference between a wallpaper-sampled material and a blur-behind one
    /// is invisible. So three of the four looked identical, and the honest fix is to offer
    /// the two that genuinely differ rather than to describe a choice that is not there.
    ///
    /// A settings file that still names a removed mode falls through to Mica. It is not
    /// worth rejecting: the value is a display preference, and the closest thing to what
    /// they picked is the material it was modelled on.
    /// </remarks>
    public static bool ApplyBackdrop(Window window, string backdrop, string mode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;

        var solid = string.Equals(backdrop, "solid", StringComparison.OrdinalIgnoreCase)
            || SystemParameters.HighContrast || !IsTransparencyEnabled();

        // Set before the material, and to Transparent only when there is going to be one.
        // Leaving it transparent in solid mode is what made "solid" translucent: the window
        // was told to composite against nothing and then painted with brushes that the theme
        // makes translucent on purpose.
        // Left transparent. What a window with no material composites against is decided in
        // ApplyBackdropOrFallback, once every surface has been resolved to an opaque colour.
        // Deciding it here means guessing at a colour the theme has not resolved yet, and
        // guessing white is what made a dark solid window unreadable: the dark translucent
        // surfaces composited against white and turned pale, so the light text ended up on a
        // pale background.
        if (HwndSource.FromHwnd(handle) is HwndSource source)
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;

        var dark = ResolveDarkMode(mode) ? 1 : 0;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        if (solid)
        {
            DisableLegacyBackdrop(handle);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                var none = 1;
                _ = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref none, sizeof(int));
            }
            return false;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var rounded = 2;
            _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            DisableLegacyBackdrop(handle);
            // DWMSBT_MAINWINDOW. The TabbedWindow and TransientWindow variants are gone from
            // the picker; a file naming them lands here, which is the right answer anyway.
            var backdropType = 2;
            if (DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdropType, sizeof(int)) == 0) return true;
        }

        // Windows 10 has no Mica compositor. Acrylic is its native Fluent material and is
        // the closest equivalent, so the one choice still means "the system's material".
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && EnableLegacyAcrylic(handle, ResolveDarkMode(mode));
    }

    /// <summary>
    /// Applies the material, and when there is none, makes the window actually opaque.
    /// </summary>
    /// <remarks>
    /// The theme paints its surfaces translucent on purpose, because they are meant to sit
    /// over a material. With no material underneath, "translucent" resolves against the
    /// desktop instead, which is not what someone choosing 实色 is asking for. So every
    /// surface layer is resolved to opaque, not only the page: leaving the sidebar and the
    /// cards translucent is what made a solid window still look like a material.
    /// </remarks>
    public static void ApplyBackdropOrFallback(Window window, string backdrop, string mode)
    {
        if (ApplyBackdrop(window, backdrop, mode)) return;

        // The page first, because it is what everything else sits on.
        var page = Flatten(window, "WindowBackgroundBrush", null);
        foreach (var key in OpaqueSurfaceKeys)
        {
            if (!string.Equals(key, "WindowBackgroundBrush", StringComparison.Ordinal)) Flatten(window, key, page);
        }

        // And composited against that page. Both answers come from the theme, so light and
        // dark need no special case here.
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && HwndSource.FromHwnd(handle) is HwndSource source)
            source.CompositionTarget.BackgroundColor = page;
    }

    /// <summary>
    /// Resolves one surface to an opaque colour by compositing it over what is behind it.
    /// </summary>
    /// <remarks>
    /// The obvious way to remove transparency is to set alpha to 255, and it is wrong. The
    /// dark palette's surface is white at 29 percent: its meaning is a faint lift off a dark
    /// page, and forcing it opaque produces pure white instead, which is how an opaque dark
    /// window ended up with white cards and white combo boxes in it. Compositing keeps the
    /// colour the theme was describing; it only resolves the part the theme left to whatever
    /// was underneath.
    /// </remarks>
    private static System.Windows.Media.Color Flatten(Window window, string key, System.Windows.Media.Color? background)
    {
        if (window.Resources[key] is not System.Windows.Media.SolidColorBrush brush) return System.Windows.Media.Colors.Transparent;
        var color = brush.Color;

        var resolved = background is { } behind
            ? System.Windows.Media.Color.FromRgb(
                (byte)Math.Round(color.R * color.A / 255.0 + behind.R * (255 - color.A) / 255.0),
                (byte)Math.Round(color.G * color.A / 255.0 + behind.G * (255 - color.A) / 255.0),
                (byte)Math.Round(color.B * color.A / 255.0 + behind.B * (255 - color.A) / 255.0))
            : System.Windows.Media.Color.FromRgb(color.R, color.G, color.B);

        window.Resources[key] = new System.Windows.Media.SolidColorBrush(resolved);
        return resolved;
    }

    /// <summary>
    /// The layers that make up a window's surface, in painting order. Foreground, border
    /// and accent colours are deliberately absent: their transparency is a drawing choice
    /// that reads correctly on an opaque background.
    /// </summary>
    private static readonly string[] OpaqueSurfaceKeys =
    [
        "WindowBackgroundBrush", "SidebarBrush", "SurfaceBrush",
        "SurfaceStrongBrush", "ControlBrush", "WarningSurfaceBrush"
    ];

    private static void SetBrush(Window window, string key, string value)
        => window.Resources[key] = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));


    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private static bool IsTransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException) { return true; }
    }

    private static bool EnableLegacyAcrylic(IntPtr handle, bool dark)
    {
        var alpha = 0xC2;
        var red = dark ? 0x20 : 0xF1;
        var green = dark ? 0x29 : 0xF5;
        var blue = dark ? 0x2A : 0xF3;
        var policy = new AccentPolicy
        {
            AccentState = AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = (alpha << 24) | (blue << 16) | (green << 8) | red
        };
        return SetAccentPolicy(handle, policy);
    }

    private static void DisableLegacyBackdrop(IntPtr handle)
        => SetAccentPolicy(handle, new AccentPolicy { AccentState = AccentState.Disabled });

    private static bool SetAccentPolicy(IntPtr handle, AccentPolicy policy)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, pointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.AccentPolicy,
                Data = pointer,
                SizeOfData = size
            };
            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableAcrylicBlurBehind = 4
    }

    private enum WindowCompositionAttribute
    {
        AccentPolicy = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
