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
assets/pets/<style>/<state>-<n>.png  (optional: extra animation frames)
README.md                         (optional)
LICENSE                           (recommended)
```

All nine PNG files are required. They must be transparent RGBA PNG files with the same canvas and a real alpha channel. The recommended canvas is 238 x 238 pixels, matching the built-in pets.

## Optional animation frames

Any state may publish extra frames. The plain file name is the first frame and each further frame adds a number starting at two:

```text
assets/pets/<style>/idle.png      frame 1
assets/pets/<style>/idle-2.png    frame 2
assets/pets/<style>/idle-3.png    frame 3
```

Rules:

- A state with no extra frames is a still image, and that is not a special case: one frame is a whole sequence. Animation is therefore additive, and a package that uses it stays valid for a host that does not.
- Numbering starts at two because `idle-1.png` would be a second name for `idle.png`. One file with two names is a way to get the two out of step, so it is not part of the contract and hosts ignore it.
- A gap ends the sequence. If `idle-3.png` is missing, `idle-4.png` is not read, and the host plays the frames it has rather than skipping the hole. A partially published set therefore degrades into a shorter loop instead of an out-of-order one.
- At most eight frames per state are read; further files are ignored.
- Every frame must use the same canvas size as the first frame of that state.

Playback is the host's decision and is not part of the contract: v1 cycles at about 140 ms per frame. Frames are drawn in place, so movement has to be drawn into the artwork rather than applied as a transform, and a loop that starts and ends on the same pose avoids a visible jump. States are independent: `idle` may animate while `error` stays a single image.

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
  "min_core_version": "0.5.0",
  "update_url": "https://api.github.com/repos/OWNER/REPOSITORY/releases/latest"
}
```

`id` identifies the extension package. `style` identifies the appearance and must be unique across built-in and installed styles. Use lower-case ASCII letters, digits, dots, and hyphens, and keep the value between 2 and 64 characters. `version` follows `x.y.z` semantic versioning. `api_version` must be `1` for this specification. `min_core_version` prevents an extension from being installed by an older incompatible host.
`update_url` is optional and uses the same GitHub Release metadata contract as feature extensions.

An appearance may also be *shipped*: a package that arrives inside the application
folder instead of being installed by the user. Shipped and installed appearances are
otherwise identical and are listed together, so an appearance can move between the
two without any change to its package. Do not assume a shipped appearance is present:
the application is moving toward shipping only a small default set, and a package
that was shipped in an older release may be installed — or absent — in a newer one.

## API version evolution

`api_version` is an integer that changes only when the *contract* changes, never for
asset or metadata changes. Adding an optional manifest field, adding a new state PNG,
or improving artwork does not change it.

A future `api_version` 2 would mean the host and the package disagree about something
that cannot be ignored, such as a renamed required state. The rules are:

- A host must refuse a package whose `api_version` it does not implement, and say so
  rather than installing it and failing later at draw time.
- A package must declare the lowest `api_version` that can load it, so it stays
  installable on older hosts for as long as that is actually true.
- A new state PNG must be optional for at least one `api_version`, so packages
  published against the previous version keep working.

## Installation and lifecycle

Users install a ZIP from the Settings window under “扩展”. BalancePet extracts it to `%LOCALAPPDATA%\BalancePet\extensions\<id>\<version>`. The main program installation directory is not modified. A later main program update keeps this directory intact. Users can enable, disable, or uninstall an extension from the same page. Uninstall removes only that extension's directory.

If the selected appearance is disabled or uninstalled, BalancePet falls back to the built-in DeepSeek appearance. The extension must therefore not assume that it is always active.

The last available appearance is not removable. The pet is the entire window, so
removing the final one would leave a blank window and an empty appearance selector,
with no way back through the interface. The host refuses that uninstall and explains
why; it is not a failure of the package and the user can resolve it by installing or
enabling another appearance first.

Appearances shipped inside the application folder follow the same rule. A host may
refuse to remove a shipped appearance from the interface at all, or may allow it when
another appearance is present — both are conforming, and neither changes what a
package must contain.

## Security and compatibility rules

The host rejects path traversal, absolute ZIP paths, oversized archives, too many files, executable files, scripts, DLLs, duplicate style IDs, incomplete state sets, invalid manifests, and incompatible API versions. Extensions must not contain access tokens, cookies, user settings, or code intended for execution.

Executable code is intentionally outside this resource-only contract. Feature
extensions use the separate, versioned
[Feature Extension Specification v1](../feature-v1/README.md), with explicit
process and capability rules; a resource extension must remain installable
without executable code. Neither resource nor feature compatibility requires a
shared visual theme, logo, window layout, or typography.

## Finding packages

A resource extension is a file, so distribution is deliberately outside this
specification: publish the ZIP wherever it can be downloaded, and the user installs it
from the Settings window like any other.

Two mechanisms exist for hosts and users that want to discover packages:

- `update_url` in the manifest points at a GitHub Release, so an installed package can
  be offered an update without the user hunting for it.
- The online extension library lists packages from the official catalog. Appearances
  published in the official appearance repository are added there.

The host never installs anything on its own initiative. Discovery only ever produces
a suggestion the user acts on.

From the repository root, run this after placing your manifest and nine PNG files in an extension directory:

```powershell
.\tools\package-pet-extension.ps1 -SourceDirectory .\path\to\pet.example -OutputPath .\dist\pet.example-1.0.0.zip
```

The packer checks the manifest and all required paths before creating the ZIP. Do not publish assets whose copyright or usage terms do not permit redistribution. BalancePet's included character references are attributed in `THIRD_PARTY_NOTICES.md`.
