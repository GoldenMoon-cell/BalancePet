# BalancePet C# WPF

Native Windows implementation of BalancePet, built with C# WPF.

## Current use

Run `launch-balance-pet-csharp.bat` from the workspace root. Open the tray menu and choose `配置接口` to edit the endpoint, authentication mode, Authorization value, JSON path, refresh interval, pet image, and interaction mode. The token is stored with Windows DPAPI.

Its settings are stored at `%LOCALAPPDATA%\\BalancePet\\csharp-settings.json`; the token is protected in that file with Windows DPAPI.

The current build includes a transparent pet window, independent settings dialog, tray menu, optional Windows startup, periodic balance refresh, automatic retry for transient request failures, in-app GitHub update checks, status bubble, sound toggles, optional Windows system notifications, free-drag/locked interaction modes, character-specific hair/mouth feedback, daily usage tracking, recent usage history in the tray's `用量统计` window, balance-drop notifications, and optional Codex process start/end notifications with balance-delta estimation. State-specific PNGs can be added under `assets/pets/<style>/<state>.png`; missing states fall back to the base image. `互动动作` and `随机彩蛋` are independent: the first controls press/release motion, while the second controls short character lines, one-off inactive reminders, and a four-interaction easter egg. When enabled, `随 Windows 启动` starts the C# app directly into its tray resident mode. When enabled, `跟随 Codex 显示/隐藏` also shows the pet when Codex starts and hides it after the task process ends. `系统通知` controls low-balance, refresh-failure, and Codex-completion notifications. Codex linkage can be enabled or disabled in the settings dialog. The C# ledger is stored at `%LOCALAPPDATA%\\BalancePet\\csharp-usage-ledger.json` and contains no token. In-app updates download a release only after confirmation, validate its SHA-256 checksum, wait for the current process to exit, then replace the portable installation and restart it. They are intended for portable, user-writable folders; protected folders such as `C:\\Program Files` may require manual replacement with administrator permission.

Planned next: richer interactions and sprite/rig support. The current procedural state layer already covers idle, loading, success, low balance, error, click, Codex working/done, and inactive states while remaining compatible with the existing PNG assets.
