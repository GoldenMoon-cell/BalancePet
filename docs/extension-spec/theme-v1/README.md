# BalancePet theme extension v1

Theme extensions customize BalancePet utility windows with declarative tokens. They never load XAML, assemblies, scripts, fonts, or executable code, and they never receive provider credentials.

## Package layout

The ZIP root contains the theme manifest and token document:

```text
manifest.json
theme.json
```

Packages are limited to 4 MB, 16 entries, and the `.json`/`.png` file extensions. Paths that escape the package root are rejected.

## Manifest

```json
{
  "id": "example.theme.mica",
  "type": "theme",
  "name": "示例云母主题",
  "name_en": "Example Mica Theme",
  "version": "1.0.0",
  "api_version": 1,
  "min_core_version": "1.0.0",
  "theme_file": "theme.json",
  "author": "Example Author",
  "update_url": "https://api.github.com/repos/example/theme/releases/latest"
}
```

`id` uses 2-64 lower-case ASCII letters, digits, dots, or hyphens. `version` follows semantic versioning. The host accepts only `api_version: 1`, `type: theme`, and a root-level `theme.json`.

## Theme tokens

```json
{
  "schema_version": 1,
  "preferred_backdrop": "mica",
  "corner_radius": 8,
  "light": {
    "window": "#E8EBF1F0",
    "sidebar": "#C8DFE8E6",
    "surface": "#99FFFFFF",
    "surface_strong": "#CCFFFFFF",
    "control": "#E8FFFFFF",
    "text": "#1C292B",
    "muted": "#637174",
    "border": "#293B4B4E",
    "accent": "#13766F",
    "accent_soft": "#1F13766F",
    "danger": "#C42B1C",
    "warning_surface": "#8CEFE0BF",
    "warning_text": "#785123"
  },
  "dark": {
    "window": "#EC202A2B",
    "sidebar": "#D9182223",
    "surface": "#16FFFFFF",
    "surface_strong": "#21FFFFFF",
    "control": "#1FFFFFFF",
    "text": "#EEF4F3",
    "muted": "#A9B5B4",
    "border": "#26D3E2DF",
    "accent": "#36B5A9",
    "accent_soft": "#2936B5A9",
    "danger": "#FF6B5F",
    "warning_surface": "#3B916423",
    "warning_text": "#E5D2AE"
  }
}
```

Every color must use `#RRGGBB` or `#AARRGGBB`. `corner_radius` is limited to `0-16`. `preferred_backdrop` is one of `mica`, `mica-alt`, `acrylic`, or `solid`.

The host, not the extension, invokes Windows backdrop APIs. Windows 11 build 22621 or newer can use Mica, Mica Alt, or Acrylic; Windows 10 uses Acrylic for the transparent choices. High contrast, disabled transparency, and unsupported systems fall back to the theme's window color.

## Installation

Theme ZIPs use the same extension library and update flow as pet and feature extensions. Installed copies live at `%LOCALAPPDATA%\BalancePet\extensions\<id>\<version>`. Disabling or uninstalling an active theme falls back to the bundled `balancepet.theme.mica` theme.

Package a theme from the repository root:

```powershell
.\tools\package-theme-extension.ps1 -SourceDirectory .\path\to\example.theme -OutputPath .\dist\example.theme-1.0.0.zip
```
