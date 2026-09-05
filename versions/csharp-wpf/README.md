# BalancePet C# WPF

Current release: `v0.3.3`.

Native Windows implementation of BalancePet, built with C# WPF.

## Current use

Run `launch-balance-pet-csharp.bat` from the workspace root. Open the tray menu and choose `配置接口`. New profiles can automatically detect the read-only `/v1/usage` and New API `/api/usage/token` protocols from a relay root URL, while Custom endpoint preserves manual endpoint, authentication, and JSON-path configuration. Automatic refresh can be disabled or set to common intervals or a custom value with a 30-second minimum; manual refreshes use a separate five-second cooldown. The token is stored with Windows DPAPI.

Its settings are stored at `%LOCALAPPDATA%\\BalancePet\\csharp-settings.json`; the token is protected in that file with Windows DPAPI.

The settings dialog supports multiple API/relay monitor profiles. Each profile has its own encrypted token, refresh interval, cache, usage ledger, and low-balance threshold; the tray `当前账户` submenu selects which profile is shown in the pet bubble. The available pet labels are DeepSeek 小鲸鱼「澜汐」, ChatGPT 小白龙「霁珑」, MiniMax 小海螺「绯音」, Gemini 小星猫「星璃」, and Grok 小恶魔「烬斧」. Simultaneous AI tasks are reference-counted globally, while resumed turns from the same session replace an abandoned turn so a missed stop event cannot leave a duplicate task count.

The current build includes a transparent pet window, independent settings dialog, tray menu, and quick appearance switching for DeepSeek 小鲸鱼「澜汐」、ChatGPT 小白龙「霁珑」、MiniMax 小海螺「绯音」、Gemini 小星猫「星璃」 and Grok 小恶魔「烬斧」. It supports multiple independent API/relay monitor profiles, optional Windows startup, periodic balance refresh, automatic retry for transient request failures, in-app GitHub update checks, status bubble, sound toggles, optional Windows system notifications, free-drag/locked interaction modes, daily usage tracking, recent usage history, balance-drop notifications, and optional automatic AI task notifications. State-specific PNGs can be added under `assets/pets/<style>/<state>.png`; missing states fall back to the base image. Multiple profiles refresh in parallel while the pet shows the selected profile; task state stays working until all active tasks finish. `互动动作` and `随机彩蛋` are independent. In-app updates download and validate the SHA-256 checksum of the matching release asset. User-writable installations replace the current directory from the ZIP; protected installations such as `C:\\Program Files` launch the `Setup.exe` installer with UAC and pass the current directory as its target. Configuration, DPAPI tokens, caches and ledgers remain under `%LOCALAPPDATA%\\BalancePet`.

For AI client compatibility, the same task bridge also listens on `BalancePet.Task.v1`. Clients without a Codex CLI can call the bundled `tools/balancepet-task.ps1` with `start` and `stop`, passing a stable task ID and provider name. The bundled Hook installer supports Gemini CLI, Qwen Code, and Claude Code. Only lifecycle metadata is accepted; prompts, replies, credentials, and network requests are never forwarded to BalancePet.

Login status integration listens on the current user's `BalancePet.Account.v1` named pipe. Clients may call `tools\balancepet-account.ps1` with `login` or `logout`, account type (`official`, `official-api`, `relay-api`, or `third-party`), provider, endpoint, optional account label, optional reported balance, and an optional SHA-256 token fingerprint. Plaintext tokens, cookies, prompts, and replies are never accepted. Known first-party API hosts are classified locally. A matching local monitor is selected automatically; unmatched relay APIs are shown as third-party and prompt the user to add them manually in settings.

Windows does not expose a universal login-status API for AI clients. A client adapter or hook must report login changes through the script or named pipe; BalancePet does not inspect browser cookies, client authentication files, or process memory. Official API providers also do not share a standard balance endpoint, so the reporting client may include a known balance while matched relay accounts use their existing BalancePet monitor configuration.

Planned next: richer interactions and sprite/rig support. The current procedural state layer already covers idle, loading, success, low balance, error, click, Codex working/done, and inactive states while remaining compatible with the existing PNG assets.
