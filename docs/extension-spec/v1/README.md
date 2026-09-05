# BalancePet Resource Extension Specification v1

This specification defines a resource-only extension package for BalancePet. It is intentionally independent of the main program installation directory. The v1 host loads PNG assets and reads the manifest only; it never loads DLLs, scripts, or executable code from an extension.

## Package layout

The ZIP root must contain `manifest.json` and the following directory:

```text
manifest.json
assets/pets/<style>/idle.png
assets/pets/<style>/loading.png
assets/pets/<style>/success.png
assets/pets/<style>/low.png
assets/pets/<style>/error.png
assets/pets/<style>/clicked.png
assets/pets/<style>/codex-working.png
assets/pets/<style>/codex-done.png
assets/pets/<style>/inactive.png
README.md                         (optional)
LICENSE                           (recommended)
```

All nine PNG files are required. They must be transparent RGBA PNG files with the same canvas and a real alpha channel. The recommended canvas is 238 x 238 pixels, matching the built-in pets.

## Manifest

`manifest.json` uses the following fields:

```json
{
  "id": "pet.example",
  "type": "pet",
  "name": "示例桌宠",
  "name_en": "Example Pet",
  "style": "pet.example",
  "version": "1.0.0",
  "api_version": 1,
  "min_core_version": "0.5.0"
}
```

`id` identifies the extension package. `style` identifies the appearance and must be unique across built-in and installed styles. Use lower-case ASCII letters, digits, dots, and hyphens, and keep the value between 2 and 64 characters. `version` follows `x.y.z` semantic versioning. `api_version` must be `1` for this specification. `min_core_version` prevents an extension from being installed by an older incompatible host.

## Installation and lifecycle

Users install a ZIP from the Settings window under “扩展”. BalancePet extracts it to `%LOCALAPPDATA%\BalancePet\extensions\<id>\<version>`. The main program installation directory is not modified. A later main program update keeps this directory intact. Users can enable, disable, or uninstall an extension from the same page. Uninstall removes only that extension's directory.

If the selected appearance is disabled or uninstalled, BalancePet falls back to the built-in DeepSeek appearance. The extension must therefore not assume that it is always active.

## Security and compatibility rules

The host rejects path traversal, absolute ZIP paths, oversized archives, too many files, executable files, scripts, DLLs, duplicate style IDs, incomplete state sets, invalid manifests, and incompatible API versions. Extensions must not contain access tokens, cookies, user settings, or code intended for execution.

Code extensions and an online extension catalog are intentionally outside v1. A future code-extension API must use a separate versioned sandbox contract and explicit permissions; a resource extension must remain installable without it.

## Building a package

From the repository root, run this after placing your manifest and nine PNG files in an extension directory:

```powershell
.\tools\package-pet-extension.ps1 -SourceDirectory .\path\to\pet.example -OutputPath .\dist\pet.example-1.0.0.zip
```

The packer checks the manifest and all required paths before creating the ZIP. Do not publish assets whose copyright or usage terms do not permit redistribution. BalancePet's included character references are attributed in `THIRD_PARTY_NOTICES.md`.
