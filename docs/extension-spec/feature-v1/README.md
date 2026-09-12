# BalancePet Feature Extension Specification v1

This specification defines an out-of-process feature extension. It is separate
from the resource-only pet extension contract in `../v1`. A feature extension
must never be loaded into the BalancePet process; the host starts its declared
entry point as a separate process and passes only the documented local data
directory.

The contract is intentionally about interoperability and safety, not visual
design. A community extension may use any UI toolkit, window size, theme,
typography, logo, icon, language, or interaction model. The host only requires
the manifest, process, capability, update, and local-data rules in this
document. Official extensions may share a visual language, but that is not a
plugin compatibility requirement.

In other words, the normative surface is deliberately small: manifest fields,
API/capability compatibility, the `--data-dir` launch contract, the sanitized
Usage Event v1 files, update-package validation, and process/security behavior.
Window chrome, controls, charts, branding, translations, and accessibility
details remain extension-owned.

## Naming

The host currently accepts feature IDs under the following namespace:

```text
repository:  BalancePet-Ext-<Category>-<Name>
manifest id: balancepet.ext.<category>.<name>
```

`id` and feature names use lower-case ASCII letters, digits, dots, and hyphens.
The short `balancepet.ext.<category>.<name>` form is used by the official
extensions. Community extensions should add a stable publisher segment after
`balancepet.ext` to avoid collisions, for example
`balancepet.ext.example-author.feature.clipboard-tools`. The prefix is a
syntax and collision-avoidance convention, not a cryptographic publisher
signature: the host does not authenticate ownership of a publisher segment.

## Package layout

```text
manifest.json
BalancePet.UsageAnalytics.exe  (or the declared entry point)
README.md
LICENSE
```

The package may include dependent runtime files and non-executable resources.
The host must reject paths outside the extension directory and must not load
extension DLLs into the host process. The host currently limits a package to
500 MB total, 100 MB per file, and 4096 archive entries; duplicate paths,
absolute paths, and traversal paths are rejected. Installer-hostile script and
launcher types (`.bat`, `.cmd`, `.com`, `.hta`, `.js`, `.jse`, `.msi`, `.ps1`,
`.scr`, `.vbs`, `.wsf`, `.wsh`) are also rejected. A feature executable may
ship ordinary DLL dependencies, but they are loaded only by that extension's
own process.

The declared `entrypoint` must be a file in the package root. Runtime
dependencies and UI assets may live below the root, but the extension must not
assume that the host's installation directory, current working directory, or
another extension's files are available.

## Manifest

```json
{
  "id": "balancepet.ext.feature.usage-analytics",
  "type": "feature",
  "name": "用量统计",
  "name_en": "Usage Analytics",
  "version": "0.2.10",
  "api_version": 1,
  "min_core_version": "0.7.7",
  "update_url": "https://api.github.com/repos/OWNER/REPOSITORY/releases/latest",
  "entrypoint": "BalancePet.UsageAnalytics.exe",
  "capabilities": ["usage.read"]
}
```

The values above mirror the current official Usage Analytics package; a
third-party extension chooses its own extension version and compatibility
floor. `version` is the extension's own semantic version. It is not the
BalancePet core version. `min_core_version` is a compatibility floor; a
compatible plugin update does not require a core update. `update_url` is
optional and, when present, must be an HTTPS GitHub
`api.github.com/repos/<owner>/<repo>/releases/latest` endpoint. The host only
reads release metadata and a ZIP asset; it never sends settings, tokens,
prompts, replies, or usage data. The downloaded package still passes the
normal manifest and archive safety checks before installation.
The built-in checker looks for a non-draft release whose tag is `vX.Y.Z` (or
`X.Y.Z`) and a `.zip` asset whose filename contains the exact version. A
`sha256:<hex>` asset digest is verified when GitHub provides one.

`api_version` is the compatibility boundary for the host contract. A v1 host
accepts only `api_version: 1`; an extension must not silently assume newer host
behavior. `capabilities` is an explicit allow-list. In v1 the only supported
capability is `usage.read`; unknown capabilities are rejected instead of being
granted implicitly. Future capabilities require a documented API revision.

The host may install multiple versions of the same extension ID side by side,
but launches the highest installed semantic version when that ID is enabled.
The `.disabled` marker applies to the ID as a whole; v1 has no per-version
selection UI. An extension should not store durable state inside its extracted
version directory because updates may replace it; use a namespaced directory
under `%LOCALAPPDATA%\BalancePet` only after that storage contract is
documented by a future API revision.

## Usage event protocol

The host writes sanitized events to `%LOCALAPPDATA%\\BalancePet\\usage-events.ndjson`.
Each line is one JSON object conforming to `event.schema.json`. Events contain
counts and timings only: prompts, replies, tokens, cookies, API keys and other
credentials must never be written. A feature extension receives the data
directory path, not provider credentials.

The first event kind is `llm_request`:

```json
{
  "schema": "balancepet.usage.v1",
  "event_id": "uuid",
  "occurred_at": "2026-09-05T12:00:00+08:00",
  "kind": "llm_request",
  "provider": "Codex",
  "account_id": "profile-id",
  "model": "model-name",
  "success": true,
  "input_tokens": 64100,
  "output_tokens": 904,
  "cache_read_tokens": 9000,
  "cache_write_tokens": 0,
  "duration_ms": 98000,
  "time_to_first_token_ms": 18500,
  "tool_calls": 0,
  "steps": 5
}
```

All numeric counters are non-negative. `input_tokens` is the complete input
total, including any cache-read portion. Therefore the v1 cache convention is
`cache_read_tokens / input_tokens` (only when input is greater than zero), and
uncached input is `input_tokens - cache_read_tokens`; `cache_write_tokens` is a
separate cache-creation series. `output_tokens` plus `duration_ms` can be used
for a throughput estimate. `time_to_first_token_ms` is optional client data;
the host does not invent it when the client does not report it. Missing
counters mean “not reported”, not zero from the provider. The host may rotate
the file to `usage-events-<timestamp>.ndjson`; extensions should read the
`usage-events*.ndjson` set, tolerate a partially written last line, and
deduplicate by `event_id` when combining files. Rescan the set when the user
presses Refresh.

The host may create a lifecycle-only event when a trusted local task bridge
knows that a request finished but the client did not provide token counters.
Such an event contributes to request counts and duration only; it must not be
interpreted as a zero-token provider response.

## Security and lifecycle

- Feature extensions run out of process; the host never passes provider/API
  tokens to them. This is process isolation and input minimization, not an
  operating-system sandbox: an installed executable still runs with the
  current user's normal Windows permissions and inherited environment/network
  access. Users should install packages they trust.
- The host validates requested capabilities before installation and can stop
  an extension process at any time. v1 does not provide a UI-embedding or
  permission-escalation mechanism.
- Extension packages are kept in the user library at
  `<BalancePet install directory>\\extension-library`. The Settings window scans
  the top-level `.zip` files in that folder. Copying a ZIP there (or using
  “Import ZIP to library”) only makes it available for selection; the user
  explicitly chooses **Install selected** before files are extracted.
- Installed extensions are extracted under `%LOCALAPPDATA%\\BalancePet\\extensions`
  and can be updated, disabled, or uninstalled independently of the core.
  Uninstall removes only the extracted runtime copy and keeps the library ZIP,
  so it can be installed again later.
- A feature extension must keep working when no events are available and must
  not treat missing data as an error.
- The host launches the entry point with `--data-dir <path>` and a working
  directory equal to the extension root. The argument is a local metadata
  directory, not a credentials directory. The executable inherits the normal
  current-user Windows permissions; this is not an OS sandbox or code-signing
  boundary. Extensions must treat unknown arguments as forward-compatible and
  must not require interactive stdin.
- Installing the same ID and version replaces that extracted version. Other
  versions remain side by side, and the host selects the highest installed
  version when the ID is enabled. A host restart or repeated launch reuses an already
  running instance of the selected executable instead of opening duplicates.
- Closing, disabling, uninstalling, or updating an extension is a host action;
  the extension must release files and child processes when its process exits.
  The host does not promise graceful shutdown indefinitely.
- The host does not provide a plugin-to-host UI embedding API in v1. Feature
  extensions own their windows and may be headless, tray-based, or graphical.

The v1 host implementation and the usage event writer were introduced in the
BalancePet `0.6.x` line. Extension library grouping and independent update
checks are available in the `0.7.x` line; the current core implementation is
`0.7.8`, and the current official Usage Analytics package is `0.2.10`. The
Usage Analytics extension can be developed and packaged independently against
this contract.

## Compatibility checklist for third-party authors

Before publishing a package, verify that:

1. `manifest.json` is at the ZIP root and passes the schema above.
2. `id`, `version`, `api_version`, `min_core_version`, `entrypoint`, and
   `capabilities` are present and valid.
3. The entry point runs out of process with only `--data-dir` and does not
   require BalancePet private classes or DLL loading.
4. Missing, rotated, empty, or partially written data files are handled without
   crashing; refresh/retry is safe.
5. The package contains no credentials, prompts, replies, cookies, or scripts
   intended to be executed by the host installer, and stays within the archive
   size, entry-count, and forbidden-extension limits above.
6. The release ZIP name contains the same semantic version as the manifest when
   using the built-in GitHub update checker.
