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
        ApplyResources(window, theme, settings.ThemeMode, settings.ThemeCompact);
        window.SourceInitialized += (_, _) => ApplyBackdropOrFallback(window, settings.ThemeBackdrop, settings.ThemeMode);
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

    public static void ApplyResources(Window window, ThemeExtensionInfo theme, string mode, bool compact)
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
        window.Resources["ThemeSectionSpacing"] = compact ? 12d : 18d;
        window.Resources["ThemeControlHeight"] = compact ? 28d : 32d;
    }

    public static bool ApplyBackdrop(Window window, string backdrop, string mode)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        if (HwndSource.FromHwnd(handle) is HwndSource source)
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;

        var dark = ResolveDarkMode(mode) ? 1 : 0;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

        var solid = string.Equals(backdrop, "solid", StringComparison.OrdinalIgnoreCase)
            || SystemParameters.HighContrast || !IsTransparencyEnabled();
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
            var backdropType = string.Equals(backdrop, "mica-alt", StringComparison.OrdinalIgnoreCase)
                ? 4
                : string.Equals(backdrop, "acrylic", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdropType, sizeof(int)) == 0) return true;
        }

        // Windows 10 has no Mica compositor. Acrylic is its native Fluent
        // material and provides the equivalent OS-aware background treatment.
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
            && EnableLegacyAcrylic(handle, ResolveDarkMode(mode), string.Equals(backdrop, "mica-alt", StringComparison.OrdinalIgnoreCase) || string.Equals(backdrop, "acrylic", StringComparison.OrdinalIgnoreCase));
    }

    public static void ApplyBackdropOrFallback(Window window, string backdrop, string mode)
    {
        ApplyBackdropOverlay(window, backdrop, mode);
        if (ApplyBackdrop(window, backdrop, mode)) return;
        if (window.Resources["WindowBackgroundBrush"] is not System.Windows.Media.SolidColorBrush current) return;
        var color = current.Color;
        window.Resources["WindowBackgroundBrush"] = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(byte.MaxValue, color.R, color.G, color.B));
    }

    private static void ApplyBackdropOverlay(Window window, string backdrop, string mode)
    {
        if (!string.Equals(backdrop, "mica-alt", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(backdrop, "acrylic", StringComparison.OrdinalIgnoreCase)) return;

        // Mica Alt and Acrylic can produce a much darker compositor surface
        // than regular Mica. Use one shared, high-opacity page layer for the
        // sidebar and content area, then ensure text tokens remain readable on
        // that resolved surface. The normal Mica palette is left untouched.
        var dark = ResolveDarkMode(mode);
        var compositorBase = dark ? MediaColor.FromRgb(0x1C, 0x24, 0x25) : Colors.White;
        var page = Composite(ReadColor(window, "WindowBackgroundBrush"), compositorBase);
        var surface = Composite(ReadColor(window, "SurfaceBrush"), page);
        var surfaceStrong = Composite(ReadColor(window, "SurfaceStrongBrush"), page);
        var control = Composite(ReadColor(window, "ControlBrush"), page);
        var border = Composite(ReadColor(window, "BorderBrush"), page);

        SetColor(window, "WindowBackgroundBrush", Opaque(page, 0xF2));
        SetColor(window, "SidebarBrush", Opaque(page, 0xF2));
        SetColor(window, "SurfaceBrush", Opaque(surface, 0xF8));
        SetColor(window, "SurfaceStrongBrush", Opaque(surfaceStrong, 0xFA));
        SetColor(window, "ControlBrush", Opaque(control, 0xFC));
        SetColor(window, "BorderBrush", Opaque(border, 0xB0));
        SetColor(window, "TextBrush", EnsureContrast(ReadColor(window, "TextBrush"), page, 4.5, dark));
        SetColor(window, "MutedBrush", EnsureContrast(ReadColor(window, "MutedBrush"), page, 3.0, dark));
    }

    private static void SetBrush(Window window, string key, string value)
        => window.Resources[key] = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));

    private static MediaColor ReadColor(Window window, string key)
        => window.Resources[key] is SolidColorBrush brush ? brush.Color : Colors.Transparent;

    private static void SetColor(Window window, string key, MediaColor color)
        => window.Resources[key] = new SolidColorBrush(color);

    private static MediaColor Opaque(MediaColor color, byte alpha)
        => MediaColor.FromArgb(alpha, color.R, color.G, color.B);

    private static MediaColor Composite(MediaColor foreground, MediaColor background)
    {
        var alpha = foreground.A / 255.0;
        return MediaColor.FromRgb(
            (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static MediaColor EnsureContrast(MediaColor candidate, MediaColor background, double minimumRatio, bool dark)
    {
        var resolved = MediaColor.FromRgb(candidate.R, candidate.G, candidate.B);
        if (ContrastRatio(resolved, background) >= minimumRatio) return resolved;
        return dark ? MediaColor.FromRgb(0xF2, 0xF7, 0xF6) : MediaColor.FromRgb(0x1C, 0x29, 0x2B);
    }

    private static double ContrastRatio(MediaColor first, MediaColor second)
    {
        var light = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var dark = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(MediaColor color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

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

    private static bool EnableLegacyAcrylic(IntPtr handle, bool dark, bool strongerTint)
    {
        var alpha = strongerTint ? 0xDD : 0xC2;
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
