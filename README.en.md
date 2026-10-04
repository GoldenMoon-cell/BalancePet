# BalancePet

A balance desktop pet for Windows. The project currently maintains only the C# WPF implementation: it queries the balance API offered by a relay provider at a configured interval and shows the state and the balance as an interactive desktop pet, without opening the provider's website.

Current release: v1.6.1; previous release: v1.5.0.

## 📊 Project statistics

<div align="center">
<table>
<tr>
<td align="center"><a href="https://github.com/GoldenMoon-cell/BalancePet/stargazers"><img src="https://img.shields.io/github/stars/GoldenMoon-cell/BalancePet?style=flat-square&label=Stars" alt="GitHub stars"></a></td>
<td align="center"><a href="https://github.com/GoldenMoon-cell/BalancePet/network/members"><img src="https://img.shields.io/github/forks/GoldenMoon-cell/BalancePet?style=flat-square&label=Forks" alt="GitHub forks"></a></td>
<td align="center"><a href="https://github.com/GoldenMoon-cell/BalancePet/issues"><img src="https://img.shields.io/github/issues/GoldenMoon-cell/BalancePet?style=flat-square&label=Open%20issues" alt="Open issues"></a></td>
</tr>
<tr>
<td align="center"><a href="https://github.com/GoldenMoon-cell/BalancePet/releases"><img src="https://img.shields.io/github/downloads/GoldenMoon-cell/BalancePet/total?style=flat-square&label=Downloads" alt="GitHub downloads"></a></td>
<td align="center"><a href="https://github.com/GoldenMoon-cell/BalancePet/releases/latest"><img src="https://img.shields.io/github/v/release/GoldenMoon-cell/BalancePet?style=flat-square&label=Latest%20release" alt="Latest release"></a></td>
<td align="center"><a href="https://github.com/GoldenMoon-cell/BalancePet/blob/main/LICENSE"><img src="https://img.shields.io/github/license/GoldenMoon-cell/BalancePet?style=flat-square&label=License" alt="License"></a></td>
</tr>
</table>
</div>

## ✨ Features

- Balance endpoint presets: common endpoints can be detected automatically, and the generic `/v1/usage`, New API `/api/usage/token`, the official DeepSeek platform or a fully custom configuration can also be chosen. Choosing the official DeepSeek platform needs only an API key — the endpoint address, the JSON paths and the currency are filled in automatically.
- Multi-account monitoring: several API/relay accounts can be added in Settings, each with its own token, refresh interval, cache, usage and low-balance threshold; the pet shows the currently selected account, and the tray can switch it quickly.
- Credential protection: tokens are encrypted with Windows DPAPI for the current user and are never written to the project configuration in plain text.
- Pet interaction: stays on top, free dragging, edge snapping, locked interaction, click to refresh and a status bubble; interaction effects and random easter eggs can each be turned off independently.
- Changelog notices: changes to specifications, documents and published content that **belong to no release** are recorded in the `notices.json` beside the appearance repository, read by the application when it can reach the network; when there are new entries the pet mentions them once, and they are handed to the Notification Center extension to display (from 1.6.1 the application no longer carries a changelog window of its own).
  - The window lists the date, category, title and summary of each change, and an "Open" button jumps to the corresponding commit or file.
  - Version updates of the application and of extensions are still handled by "Check for updates", and do not appear here a second time.
  - The notification switch is among the interaction switches in the settings window.
  - A fresh installation stays quiet: entries published before it was installed have by definition already been missed, so they are not announced again.
  - The format is in [docs/extension-spec/notices-v1/README.md](docs/extension-spec/notices-v1/README.md).
- Notifications and statistics: low-balance alerts, daily usage and recent usage history; a New API-compatible relay can synchronise the server's actual quota from the read-only token log, while other endpoints still show what the upstream reports.
- Settings migration: a settings file that contains no token can be imported and exported, which suits switching relay providers or moving to another computer.
- Update management: GitHub Releases can be checked at every startup, daily, weekly or manually only; the download is verified against SHA-256 before updating. A writable installation directory is replaced directly from the ZIP, while a protected directory falls back to an administrator installer.
- Bilingual interface: the installer offers Simplified Chinese or English at startup, and the application language can be switched at any time in the pet settings.
- Tray residency and starting with Windows: the tray supports configuring, refreshing, viewing statistics and exiting; "Change appearance" in the context menu switches quickly between the appearances already installed.
- Appearance packages: the application ships exactly one appearance of its own, the built-in placeholder, so that the window always has something to draw; **every character appearance is published as an installable appearance package**, including DeepSeek 小鲸鱼「澜汐」 and ChatGPT 小白龙「霁珑」, which the application used to distribute.
  - An appearance is chosen or uninstalled on the "Pet & interaction" page, and can also be found under the "Appearances" category of the online plugin catalog.
  - When upgrading from an older version, appearances already in the installation directory are converted into installed packages automatically; the appearance in use is not lost and does not have to be downloaded again.
- Appearance preview and animation: the "Pet & interaction" page of the settings window shows the current appearance in its nine states as a 3×3 grid; a state with extra frames is labelled with its frame count and loops on the pet.
- Appearance lines: each appearance can carry its own easter-egg lines in a `lines.json` beside the artwork, published with the package, covering long inactivity, a click, a four-hit streak of rapid interaction, and touching the head, the face or the body.
  - The application no longer bundles copy for any particular character and keeps only one neutral set that names nobody: an appearance without this file, a file that cannot be read, or one declaring an unknown `schema_version` all fall back to it, and the pet keeps drawing as usual.
  - The same lines are also collected into the `lines.json` in the appearance repository, which the application prefers whenever it can reach the network — a package is almost entirely artwork, so republishing it just to correct one sentence would push everyone through a download of megabytes to deliver a few hundred bytes; changing a line is now one small commit, with no release and no download.
  - The format is in the "Optional lines" section of [docs/extension-spec/v1/README.md](docs/extension-spec/v1/README.md).
- AI integration: the settings window has a separate "AI integration" page.
  - DeepSeek Harness, Codex, Gemini CLI, Qwen Code and Claude Code each have a switch of their own, plus an "Other clients" fallback, so a custom CLI that is not listed does not stop working.
  - Turning a switch on writes the hook that client needs and turning it off removes it; a client that is not installed is shown in a warning colour with a note under it, and can still be turned on: the configuration is written automatically once the client appears.
- Cost attribution: every usage record carries the account that was actually billed, shown in Usage Analytics as "client · account name".
  - A relay account captures the per-request billing log and labels it "quota spent"; an official API account instead reports "total task spend" from the balance difference across the task, noting that this is a balance change rather than per-request billing.
  - When both routes have data, the per-request log wins.
- State assets: the nine state images (idle, querying, query succeeded, low balance, query failed, clicked, task running, task finished, inactive) are required content of an appearance package.
  - Those published as appearance packages so far are DeepSeek 小鲸鱼「澜汐」, ChatGPT 小白龙「霁珑」, MiniMax 小海螺「绯音」, Gemini 小星猫「星璃」, Grok 小恶魔「烬斧」, Claude 小书灵「丹笺」, Kimi 小棱镜「虹谱」, Qwen 小折扇「绀华」, Ernie 小病书灵「青绡」, GLM 小方灵「青棱」, GPT Image 2 小墨龙「玄珏」, Llama 小羊驼「绒眠」, MiMo 小兔码师「橙析」 and Seedance 小星晶「澄芽」; Mistral, OpenCode, Perplexity and RWKV have also been published as appearance packages, making eighteen sets in all.
  - The appearance package index is published in the separate [BalancePet-Pets](https://github.com/GoldenMoon-cell/BalancePet-Pets) repository, kept apart from the main repository's plugin catalog.

## 🧩 Extensions

`v0.5.0` introduced the basis of resource-only pet extensions; `v0.6.0` added the separate process host for feature extensions and the scrubbed usage event pipeline.

The current plugin catalog offers Usage Analytics `v0.4.0`, Notification Center `v0.6.2`, the Mica theme `v1.0.0` and Browser Bridge `v1.0.0`.

Open the "Extensions" page of the settings window: it is split into two tabs, "Online plugin catalog" and "Local extension features". The first browses downloadable extensions by category — features, appearances, themes and browser — while the second lists the packages already downloaded to this computer and scans the top-level `.zip` files in the `extension-library` folder beside the application directory.

An installed appearance package does not appear again under "Local extension features": it is chosen or uninstalled back on the "Pet & interaction" page, so there is only one place to manage it.

Feature, resource and theme extensions can be downloaded from the online catalog and installed after local validation; a browser extension is explicitly marked as a "browser extension" and offers only a button that opens its separate repository, because the application cannot install or load it directly.

When the catalog is unavailable, the last valid cache is shown, and importing a local ZIP still works.

Each extension entry offers icon buttons on the right for install/uninstall, enable/disable, update and launch; different versions of the same extension are merged into one entry.

Extension updates can be set independently to check at every startup, daily, weekly or manually only; the update address is declared by the extension manifest's `update_url`, and the download still goes through the normal ZIP and manifest validation.

Uninstall removes only the installed copy under `%LOCALAPPDATA%\BalancePet\extensions` and keeps the ZIP in the extension library, so the extension can be installed again later.

Feature extensions are never loaded into the main process; they can read scrubbed local data only through the capabilities they declare.

The plugin catalog specification is in [docs/extension-spec/feature-v1/README.md](docs/extension-spec/feature-v1/README.md).



| Extension | Repository | Description |
|---|---|---|
| Usage Analytics | [UsageAnalytics](https://github.com/GoldenMoon-cell/BalancePet-Ext-Feature-UsageAnalytics) | `v0.4.0` history and statistics for queries and spend |
| Notification Center | [NotificationCenter](https://github.com/GoldenMoon-cell/BalancePet-Ext-Feature-NotificationCenter) | `v0.6.2` changelog notices, tasks, accounts, balance and system messages |
| Mica theme | [Theme-Mica](https://github.com/GoldenMoon-cell/BalancePet-Ext-Theme-Mica) | `v1.0.0` Mica, Mica Alt and acrylic effects |
| Browser bridge | [BrowserBridge](https://github.com/GoldenMoon-cell/BalancePet-BrowserBridge) | `v1.0.0` browser extension that synchronises the session to the pet |
| Appearance and theme packages | [BalancePet-Pets](https://github.com/GoldenMoon-cell/BalancePet-Pets) | eighteen appearances, plus the appearance catalog and the lines |


A theme extension contains only colour, corner-radius and material tokens validated against an allow-list and loads no arbitrary XAML or code; the Windows Mica effect is always invoked by the application through the system API.

Use [tools/package-pet-extension.ps1](tools/package-pet-extension.ps1) to package any nine-state pet appearance, [tools/package-shipped-pets.ps1](tools/package-shipped-pets.ps1) to turn the appearances the application used to carry into release packages in one batch, [tools/build-skin-catalog.ps1](tools/build-skin-catalog.ps1) to generate the appearance package index, and [tools/package-theme-extension.ps1](tools/package-theme-extension.ps1) to package a theme.

Extra animation frames in an appearance package are named `idle.png`, `idle-2.png` and so on; a gap in the numbering ends the sequence, and at most eight frames are read. An application that knows nothing of animation still reads only the first frame, so adding animation cannot stop an older version from opening a new package.

The publishing process for resource extensions is in [skins/PUBLISHING.md](skins/PUBLISHING.md).

The Usage Analytics plugin lives in `extensions/BalancePet-Ext-Feature-UsageAnalytics/` and is launched from "Usage" in the context menu; it watches the event files for changes and refreshes automatically at an interval of no more than 60 seconds.

A task completion event can record the request count and the elapsed time automatically;


To measure real tokens, cache hits and time to first token (TTFT), the client also has to report the Usage.v1 fields itself.

The application also exports the local balance-change ledger its base window uses as a scrubbed `balance-usage.v1.json`, so an extension can show "today's spend"; that figure is an estimate from a balance drop, not a relay bill.

The application receives only metadata such as counts and timings; it never receives prompts, replies or tokens.

The feature extension protocol defines only the manifest, `--data-dir`, Usage Event v1, Balance Usage v1, capabilities and lifecycle; a third-party extension is free to choose its own UI toolkit, theme, logo, window layout and interaction style.



If a relay provider's cost detail endpoint needs a web session, the `v1.0.0` extension is available from the separate [BalancePet Browser Bridge repository](https://github.com/GoldenMoon-cell/BalancePet-BrowserBridge).

The application release also carries the same extension ZIP as a convenience download.

In Edge or Chrome, enable developer mode on the extensions page and load the unpacked extension directory, then generate a pairing code under "Advanced: read the session from this computer's browser" in the BalancePet settings, click the extension in a tab that is signed in to the relay provider, and synchronise.

Cookies are passed to BalancePet only over the local loopback address and are stored encrypted with DPAPI; there is no need to copy a cookie by hand.


| Specification | Contents |
|---|---|
| [Resource extension package format](docs/extension-spec/v1/README.md) | the directory layout of appearance and resource packages, state image names, the optional `lines.json` |
| [Appearance repository documents](docs/extension-spec/appearance-v1/README.md) | the appearance repository's two online documents: the appearance catalog and the lines |
| [Feature extension protocol](docs/extension-spec/feature-v1/README.md) | manifest, `--data-dir`, capabilities, lifecycle and the event pipeline |
| [Declarative theme protocol](docs/extension-spec/theme-v1/README.md) | the allow-list of colour, corner-radius and material tokens |
| [Changelog notice format](docs/extension-spec/notices-v1/README.md) | `notices.json`: changes that belong to no release |
## ▶️ Running

`BalancePet-<version>-Setup.exe` from the GitHub Release is recommended. The installer first offers a choice of Simplified Chinese or English and then presents the installation in the chosen language; it bundles the .NET runtime, and can install for the current user only, or request administrator rights and install to an all-users directory such as `Program Files`.

The portable ZIP and a build from source likewise need no separate .NET runtime installation; the release packages use a self-contained .NET 8 Windows x64 deployment.

Building from source:

```powershell
dotnet build .\versions\csharp-wpf\BalancePet.Wpf.csproj --configuration Release
```

After building, run `launch-balance-pet.bat` in the repository root, or start the generated `BalancePet.Wpf.exe` directly. The first launch opens the configuration window; afterwards "Configure API" can be chosen from the tray menu.

## ⚙️ Configuring the API

- **Monitor accounts**: multiple accounts can be added, deleted and enabled at the top of the settings window; each account stores its own endpoint preset, token, refresh interval and threshold. Tokens are still encrypted per account with Windows DPAPI.
- **Endpoint preset**: "Automatic detection" tries the read-only `/v1/usage` and `/api/usage/token` in turn on the same site, and the matching protocol can also be chosen directly. A preset needs only the relay's root address and an API key, and the application fills in the endpoint, Bearer authentication and the balance fields; New API reads the quota ratio and the USD/CNY/token/custom-currency setting from the public status before showing a balance, and tries to synchronise per-request spend from the read-only `/api/log/token` on the same site.
- For a site that offers only `/v1/usage`, the application writes `daily_usage.actual_cost` (or `cost` when that is absent) to the Usage Analytics plugin as a scrubbed daily summary; this is not a per-request charge and cannot replace a relay's request log.
- **Custom endpoint**: after choosing "Custom endpoint", the full API address, authentication mode, request headers and JSON paths can still be configured by hand; an account from an older version migrates to this mode without loss.
- **Balance API endpoint**: the full URL of the balance query API given in the relay provider's documentation, not the website home page or a chat endpoint.
- **Authentication**: supports `Bearer Token`, a full `Authorization`, `x-api-key` and a custom header.
- **Balance JSON path**: for example, for `{ "data": { "balance": 12.3 } }` enter `data.balance`.
- **Automatic refresh interval**: can be off, 30 seconds, 1/5/15/30 minutes, 1 hour or a custom value (30 seconds minimum); entering `300`, for example, queries the balance every 5 minutes.
  - Once it is off, no background polling runs.
  - A manual refresh from the pet is not affected by this setting, but two manual refreshes are at least 5 seconds apart; the balance update after an AI task completes is an internal forced refresh.
- **Language**: "Simplified Chinese" or "English" can be chosen. Saving applies it to the settings window, the pet menu, the bubbles and Usage Analytics.
- **Network failure handling**: a request that times out, meets network trouble or receives 408/425/429/5xx is retried twice automatically, and if it still fails the last cached balance is shown when there is one.
- **Settings import/export**: JSON can be imported or exported at the bottom of the settings window; the exported file contains no access token, so the token has to be entered again on another computer.

The pet itself shows one account, and the "Current account" menu in the tray switches which one it shows; every enabled account still refreshes in the background at its own interval. The `provider` of an AI task may hold the name or the ID of a monitor account, which associates the task with that account; when nothing matches, the currently selected account is used.

Tokens, endpoint addresses and local usage data should never be committed to Git. Configuration and tokens are stored in `%LOCALAPPDATA%\BalancePet`; tokens are encrypted with Windows DPAPI.

A credential-free example is in [docs/balance-pet.example.json](docs/balance-pet.example.json). Automatic detection sends the token only to the same site the user entered and never hands it to a third-party detection service.

## 🎨 Assets

The anime character reference material used for the pet's state images all comes from the Bilibili creator `@ZipZipPipe`. Thanks to the original author for sharing it publicly; the permitted use and the licensing terms of the material are those stated on the original author's publishing page.

The state images live in:

```text
versions/csharp-wpf/assets/pets/<style>/<state>.png
```

The supported state file names are:

```text
idle.png
loading.png
success.png
low.png
error.png
clicked.png
codex-working.png
codex-done.png
inactive.png
```

The artwork must be a transparent RGBA PNG with a real alpha channel. Do not imitate transparency with a white, grey or checkerboard image; see [docs/csharp-art-pipeline.md](docs/csharp-art-pipeline.md) for details.

## 📦 Packaging

```powershell
.\tools\package-csharp-release.ps1
```

The script produces two release assets:

- `dist/BalancePet-<version>-Setup.exe`: recommended for ordinary users. It can install for the current user or for all users, choose the installation directory, and create shortcuts and an uninstall entry.
- `dist/BalancePet-<version>-win-x64.zip`: the portable package, and the payload an in-app in-place update uses.

Building the installer requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). Pass `-SkipInstaller` when only the ZIP needs to be verified locally.

The full installation, upgrade and migration steps are in [docs/UPGRADE.md](docs/UPGRADE.md). An enabled startup entry is updated to the current executable path the first time a new version runs.

An in-app update chooses its route by directory permissions: a current-user directory or another writable directory is replaced in place, while a protected directory such as `Program Files` downloads and verifies `Setup.exe` and upgrades after the user confirms UAC. Configuration, encrypted tokens and usage records always stay in `%LOCALAPPDATA%\\BalancePet`.


## 🖱️ Interaction effects

- **Interaction effects**: controls the press, bounce, slight tilt and expression states while interaction is locked; when it is off the pet can still be dragged and clicked to refresh the balance.
- **Random easter eggs**: controls the current character's own short lines, the idle hints and the rapid-interaction easter egg. Different characters, touched parts and streaks draw from separate candidate pools and avoid the lines used most recently; only four to six rapid interactions in a row trigger one easter egg, so it does not interrupt too often.
- **State switching**: a successful query briefly shows the success image before returning to idle; while the pointer holds the character down the clicked image stays up, and only on release does the refresh or the interaction feedback begin. A background automatic refresh does not reset the idle timer, and after 15 minutes without user interaction the inactive image is shown.
- **AI task state**: switched on and off per client on the "AI integration" page of the settings window.
  - BalancePet listens on a local named pipe private to the current user; the pet stays on `codex-working` while at least one task is active and switches to `codex-done` only when every task has finished or stopped, so the end of a single task cannot cut it short.
  - When that happens the balance is refreshed, and the bubble shows the client name.
  - The integration passes only start/end, the client name and the task ID; it never reads or stores prompts, replies or tokens.
- **Per-client switches**: one independent switch per client — Codex, DeepSeek Harness, Gemini CLI, Qwen Code, Claude Code, and "Other clients".
  - Turning a switch on makes BalancePet write the hook that client needs (Codex uses `~/.codex/hooks.json`, Gemini/Qwen/Claude use their own `settings.json`, and DeepSeek Harness uses a Cordis plugin); turning it off removes it.
  - A client that is not installed is shown in a warning colour with a note under it and can still be turned on: ticking it only records the intent, and the configuration is written automatically on the next launch once the client appears — no settings file is conjured up for a client that is not installed.
  - A custom CLI that calls `tools/balancepet-task.ps1` is picked up by "Other clients", so an integration that is not listed does not stop working.
- **DeepSeek Harness integration**: DeepSeek Harness has no hooks.json as Codex does, and its official extension point is a Cordis plugin, so BalancePet bundles a bridge plugin (`tools/dsh-bridge/`).
  - Normally no manual installation is needed — ticking DeepSeek Harness on the "AI integration" page is enough, and it is mounted automatically when the settings are saved.
  - It can also be run by hand: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\install-balancepet-dsh-plugin.ps1"`.
  - DSH reloads the profile by itself, but **only for a first installation**: after replacing plugin files that are already on disk, DSH has to be restarted, because ESM modules are cached by URL within the process.
  - The diagnostic log is written to `%LOCALAPPDATA%\BalancePet\dsh-bridge.log`; `bridge active` in it means the plugin has taken effect.
  - The plugin subscribes to the official `turn/start` and `turn/end` session events and reports over the same local named pipe, so its bubbles and balance refreshes behave like those of the other clients.
  - Besides start and end it also reports the **per-turn delta** of model, reasoning effort, input/output/cache-read/cache-write tokens, step count and elapsed time, so a DeepSeek Harness record in Usage Analytics is as detailed as a Codex one.
  - The plugin reports no cost: DSH counts tokens only and has no price list, so a relay route is backfilled from the application's per-request log, while an official account shows "not reported".
  - Installation only copies the plugin directory into the profile and appends a mount entry to `cordis.patch.yml`; it does not touch the profile's `package.json` and does not go online. Uninstall passes `-Action Uninstall`, and both operations back up that configuration file first.
- **Usage attribution and cost source**: every usage record carries the account it was actually billed to, shown in Usage Analytics as "client · account name".
  - Cost is measured in one of two ways, by account type: a relay account can capture the per-request billing log, recording the quota of each request (labelled "quota spent"); an official API account has no per-request billing endpoint, so the **balance difference before and after the task** gives that task's total spend instead (labelled "total task spend", noting that it is a balance change rather than per-request billing).
  - When both routes have data, the per-request log wins.
  - The balance difference is used only when the account is not a relay; the decision comes from the existing relay capability probe and needs no extra configuration.
  - For it to take effect, the official account itself has to be added to monitoring, DeepSeek for example: endpoint `https://api.deepseek.com/user/balance`, authentication `authorization`, balance path `balance_infos.0.total_balance`, currency path `balance_infos.0.currency`.
  - Note that the balance refreshes at most once every 30 seconds, so a task shorter than that interval may measure no spend.
- **Saving without a token**: the balance API access token can be left empty for now. Saving skips the balance connection test but still stores the AI task integration and other settings; configuring the token later restores balance queries.
- Bubble hints are shorter than the balance status notices, and balance queries, low-balance and error notices are not affected by the switches above.

## 🔌 Custom client integration

BalancePet does not require the matching AI client or CLI to be installed. A custom client that is not listed above is picked up by "Other clients" as long as it reports start and end as shown below.

Start a task: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\balancepet-task.ps1" start <task-id> <provider>`

Stop a task: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\balancepet-task.ps1" stop <task-id> <provider>`

For example, the client name can be `Claude Code`, `通义灵码` or `generic`. `<task-id>` should be the same in the start and end events of one task; when a stop event has no task ID, the pet matches it automatically as long as only one task is running. The script connects only to the current user's local `BalancePet.Task.v1` named pipe and opens no network port. A client that can use the named pipe directly may also send one line of JSON (`state` being `start` or `stop`): `{"state":"start","sessionId":"external:<provider>","turnId":"<task-id>","provider":"<provider>"}`. The script returns error code 2 when BalancePet is not running or "Follow AI tasks" is not ticked.


Gemini CLI, Qwen Code and Claude Code can be configured to call `tools\\balancepet-client-hook.ps1` from their lifecycle hooks. The adapter reads only `session_id` from the hook's standard input, uses it as the task ID, and always returns empty JSON; it never reads, records or forwards prompts, replies, API tokens or network requests. Gemini should use `BeforeAgent` / `AfterAgent`, while Qwen and Claude should use `UserPromptSubmit` / `Stop`; the command path must point at that script inside the current release package.


If Gemini CLI, Qwen Code or Claude Code is installed, running `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\install-balancepet-client-hooks.ps1"` in the release package root merges the current user's hook settings automatically; `-Client` can also be set to `Gemini`, `Qwen` or `Claude`. The installer deduplicates by name, keeps other settings, and creates a timestamped backup before modifying an existing settings file; the change takes effect after the client restarts.

## 🗂️ Project structure

```text
versions/csharp-wpf/  C# WPF application
tools/                release packaging scripts
docs/                 configuration examples, asset requirements, extension specifications and licence copies
```

## 📜 Origin and licence

BalancePet is an independent C# WPF rewrite. Some of the whale artwork and the interaction sounds are adapted from the MIT-licensed [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget). The original licence copy and the full attribution are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

The [LICENSE](LICENSE) in the repository root applies to BalancePet's original source code; third-party assets remain under their own licences.
