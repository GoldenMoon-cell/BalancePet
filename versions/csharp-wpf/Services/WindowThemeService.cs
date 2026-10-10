using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using BalancePet.Wpf.Models;
using Microsoft.Win32;

namespace BalancePet.Wpf.Services;

public static class WindowThemeService
{
	private enum AccentState
	{
		Disabled = 0,
		EnableAcrylicBlurBehind = 4
	}

	private enum WindowCompositionAttribute
	{
		AccentPolicy = 19
	}

	private struct AccentPolicy
	{
		public AccentState AccentState;

		public int AccentFlags;

		public int GradientColor;

		public int AnimationId;
	}

	private struct WindowCompositionAttributeData
	{
		public WindowCompositionAttribute Attribute;

		public nint Data;

		public int SizeOfData;
	}

	private const int DwmwaUseImmersiveDarkMode = 20;

	private const int DwmwaWindowCornerPreference = 33;

	private const int DwmwaSystemBackdropType = 38;

	public const string DefaultFontFamily = "Segoe UI, Microsoft YaHei UI";

	private static readonly string[] OpaqueSurfaceKeys = new string[6] { "WindowBackgroundBrush", "SidebarBrush", "SurfaceBrush", "SurfaceStrongBrush", "ControlBrush", "WarningSurfaceBrush" };

	public static void ApplyConfiguredTheme(Window window, PetSettings settings)
	{
		ThemeExtensionManager themeExtensionManager = new ThemeExtensionManager();
		themeExtensionManager.EnsureBundledThemeInstalled();
		ThemeExtensionInfo themeExtensionInfo = themeExtensionManager.GetLatestEnabled(settings.ThemeId) ?? themeExtensionManager.GetLatestEnabled("balancepet.theme.mica") ?? themeExtensionManager.GetLatestEnabled().FirstOrDefault();
		if (themeExtensionInfo != null)
		{
			ApplyResources(window, themeExtensionInfo, settings.ThemeMode);
			window.SourceInitialized += delegate
			{
				ApplyBackdropOrFallback(window, settings.ThemeBackdrop, settings.ThemeMode);
			};
		}
	}

	public static void ApplyFont(Window window, string? font)
	{
		string text = (string.IsNullOrWhiteSpace(font) ? "Segoe UI, Microsoft YaHei UI" : font.Trim());
		if (window.Resources["UiFontFamily"] is FontFamily fontFamily && string.Equals(fontFamily.Source, text, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		try
		{
			window.Resources["UiFontFamily"] = new FontFamily(text);
		}
		catch (Exception ex) when (((ex is ArgumentException || ex is UriFormatException) ? 1 : 0) != 0)
		{
			window.Resources["UiFontFamily"] = new FontFamily("Segoe UI, Microsoft YaHei UI");
		}
	}

	public static bool ResolveDarkMode(string mode)
	{
		if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			return registryKey?.GetValue("AppsUseLightTheme") is int num && num == 0;
		}
		catch (Exception ex) when (((ex is UnauthorizedAccessException || ex is IOException) ? 1 : 0) != 0)
		{
			return false;
		}
	}

	public static void ApplyResources(Window window, ThemeExtensionInfo theme, string mode)
	{
		ThemePalette themePalette = (ResolveDarkMode(mode) ? theme.Theme.Dark : theme.Theme.Light);
		SetBrush(window, "WindowBackgroundBrush", themePalette.Window);
		SetBrush(window, "SidebarBrush", themePalette.Sidebar);
		SetBrush(window, "SurfaceBrush", themePalette.Surface);
		SetBrush(window, "SurfaceStrongBrush", themePalette.SurfaceStrong);
		SetBrush(window, "ControlBrush", themePalette.Control);
		SetBrush(window, "TextBrush", themePalette.Text);
		SetBrush(window, "MutedBrush", themePalette.Muted);
		SetBrush(window, "BorderBrush", themePalette.Border);
		SetBrush(window, "AccentBrush", themePalette.Accent);
		SetBrush(window, "AccentSoftBrush", themePalette.AccentSoft);
		SetBrush(window, "DangerBrush", themePalette.Danger);
		SetBrush(window, "WarningSurfaceBrush", themePalette.WarningSurface);
		SetBrush(window, "WarningTextBrush", themePalette.WarningText);
		window.Resources["ThemeCornerRadius"] = new CornerRadius(theme.Theme.CornerRadius);
		window.Resources["ThemeControlHeight"] = 32.0;
	}

	public static bool ApplyBackdrop(Window window, string backdrop, string mode)
	{
		nint handle = new WindowInteropHelper(window).Handle;
		if (handle == IntPtr.Zero)
		{
			return false;
		}
		bool num = string.Equals(backdrop, "solid", StringComparison.OrdinalIgnoreCase) || SystemParameters.HighContrast || !IsTransparencyEnabled();
		HwndSource hwndSource = HwndSource.FromHwnd(handle);
		if (hwndSource != null)
		{
			hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
		}
		int value = (ResolveDarkMode(mode) ? 1 : 0);
		if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
		{
			DwmSetWindowAttribute(handle, 20, ref value, 4);
		}
		if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
		{
			int value2 = 2;
			DwmSetWindowAttribute(handle, 33, ref value2, 4);
		}
		if (num)
		{
			DisableLegacyBackdrop(handle);
			if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
			{
				int value3 = 1;
				DwmSetWindowAttribute(handle, 38, ref value3, 4);
			}
			return false;
		}
		if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
		{
			DisableLegacyBackdrop(handle);
			int num2 = ((backdrop == "acrylic") ? 3 : ((!(backdrop == "mica-alt")) ? 2 : 4));
			int value4 = num2;
			if (DwmSetWindowAttribute(handle, 38, ref value4, 4) == 0)
			{
				return true;
			}
		}
		if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
		{
			return EnableLegacyAcrylic(handle, ResolveDarkMode(mode));
		}
		return false;
	}

	public static void ApplyBackdropOrFallback(Window window, string backdrop, string mode)
	{
		if (ApplyBackdrop(window, backdrop, mode))
		{
			return;
		}
		Color color = Flatten(window, "WindowBackgroundBrush", null);
		string[] opaqueSurfaceKeys = OpaqueSurfaceKeys;
		foreach (string text in opaqueSurfaceKeys)
		{
			if (!string.Equals(text, "WindowBackgroundBrush", StringComparison.Ordinal))
			{
				Flatten(window, text, color);
			}
		}
		nint handle = new WindowInteropHelper(window).Handle;
		if (handle != IntPtr.Zero)
		{
			HwndSource hwndSource = HwndSource.FromHwnd(handle);
			if (hwndSource != null)
			{
				hwndSource.CompositionTarget.BackgroundColor = color;
			}
		}
	}

	private static Color Flatten(Window window, string key, Color? background)
	{
		if (!(window.Resources[key] is SolidColorBrush { Color: var color }))
		{
			return Colors.Transparent;
		}
		Color color2;
		if (background.HasValue)
		{
			Color valueOrDefault = background.GetValueOrDefault();
			color2 = Color.FromRgb((byte)Math.Round((double)(color.R * color.A) / 255.0 + (double)(valueOrDefault.R * (255 - color.A)) / 255.0), (byte)Math.Round((double)(color.G * color.A) / 255.0 + (double)(valueOrDefault.G * (255 - color.A)) / 255.0), (byte)Math.Round((double)(color.B * color.A) / 255.0 + (double)(valueOrDefault.B * (255 - color.A)) / 255.0));
		}
		else
		{
			color2 = Color.FromRgb(color.R, color.G, color.B);
		}
		Color color3 = color2;
		window.Resources[key] = new SolidColorBrush(color3);
		return color3;
	}

	private static void SetBrush(Window window, string key, string value)
	{
		window.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int valueSize);

	private static bool IsTransparencyEnabled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			return !(registryKey?.GetValue("EnableTransparency") is int num) || num != 0;
		}
		catch (Exception ex) when (((ex is UnauthorizedAccessException || ex is IOException) ? 1 : 0) != 0)
		{
			return true;
		}
	}

	private static bool EnableLegacyAcrylic(nint handle, bool dark)
	{
		int num = 194;
		int num2 = (dark ? 32 : 241);
		int num3 = (dark ? 41 : 245);
		int num4 = (dark ? 42 : 243);
		AccentPolicy policy = new AccentPolicy
		{
			AccentState = AccentState.EnableAcrylicBlurBehind,
			AccentFlags = 2,
			GradientColor = ((num << 24) | (num4 << 16) | (num3 << 8) | num2)
		};
		return SetAccentPolicy(handle, policy);
	}

	private static void DisableLegacyBackdrop(nint handle)
	{
		SetAccentPolicy(handle, new AccentPolicy
		{
			AccentState = AccentState.Disabled
		});
	}

	private static bool SetAccentPolicy(nint handle, AccentPolicy policy)
	{
		int num = Marshal.SizeOf<AccentPolicy>();
		nint num2 = Marshal.AllocHGlobal(num);
		try
		{
			Marshal.StructureToPtr(policy, num2, fDeleteOld: false);
			WindowCompositionAttributeData data = new WindowCompositionAttributeData
			{
				Attribute = WindowCompositionAttribute.AccentPolicy,
				Data = num2,
				SizeOfData = num
			};
			return SetWindowCompositionAttribute(handle, ref data) != 0;
		}
		finally
		{
			Marshal.FreeHGlobal(num2);
		}
	}

	[DllImport("user32.dll")]
	private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);
}
