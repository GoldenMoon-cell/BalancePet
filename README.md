# BalancePet

面向 Windows 的余额桌宠。项目当前只维护 C# WPF 版本：它会按设定间隔查询中转站提供的余额 API，并以可互动的桌宠显示状态和余额，不需要打开中转站网页。

当前发布版本：`v0.4.0`。

## 项目统计

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

## 功能

- 余额接口预设：可自动识别常见接口，也可选择通用 `/v1/usage`、New API `/api/usage/token` 或完整自定义配置。
- 多账户监控：可在设置中新增多个 API/中转站账户，每个账户独立令牌、刷新间隔、缓存、用量和低余额阈值；桌宠聚合显示当前选中账户，托盘可快速切换。
- 凭证保护：令牌由 Windows DPAPI 按当前用户加密保存，不以明文写入项目配置。
- 桌宠交互：置顶显示、自由拖动、边缘吸附、锁定互动、点击刷新和状态气泡；可独立关闭互动动作或随机彩蛋。
- 通知与统计：低余额提示、系统通知、每日用量和最近使用记录。
- 配置迁移：可导入/导出不含令牌的设置文件，适合切换中转站或迁移到另一台电脑。
- 更新管理：可选每次启动、每天、每周或仅手动检查 GitHub Release；更新前会下载并校验 SHA-256。可写安装目录直接替换 ZIP，受保护目录会改用管理员安装器。
- 双语界面：安装器启动时可选择简体中文或 English；桌宠设置中也可随时切换应用语言。
- 托盘驻留与开机启动：支持从托盘配置、刷新、查看统计和退出；右键菜单的“切换形象”可快速切换当前已完成的澜汐、霁珑、绯音、星璃和烬斧；可自动跟随 AI 客户端任务的开始和结束。
- AI 登录状态识别：客户端可通过本机命名管道报告官方账户、官方 API 或第三方 API 登录；已保存的 API 账户按令牌指纹或唯一接口自动匹配并切换，未收录的第三方 API 会提示添加到本地。BalancePet 不读取网页登录凭据，也不接收明文令牌。
- 状态素材：当前支持 DeepSeek 小鲸鱼「澜汐」、ChatGPT 小白龙「霁珑」、MiniMax 小海螺「绯音」、Gemini 小星猫「星璃」和 Grok 小恶魔「烬斧」的待机、加载、成功、低余额、错误、点击、Codex 工作/完成和闲置状态图；其他已登记形象会在素材完整前保持禁用。

## 运行

推荐使用 GitHub Release 中的 `BalancePet-<版本>-Setup.exe`。安装器首先提供简体中文与 English 选择，随后以所选语言展示安装流程；它会打包 .NET 运行时，可选择仅为当前用户安装，或请求管理员权限后安装到 `Program Files` 等全用户目录。

便携 ZIP 和开发环境构建同样不需要额外安装 .NET 运行时；发布包使用自包含 .NET 8 Windows x64 部署。

开发环境构建：

```powershell
dotnet build .\versions\csharp-wpf\BalancePet.Wpf.csproj --configuration Release
```

构建后运行根目录的 `launch-balance-pet.bat`，或直接启动生成的 `BalancePet.Wpf.exe`。首次启动会打开配置窗口；之后可从托盘菜单选择“配置接口”。

## 配置接口

- **监控账户**：设置窗口顶部可以新增、删除和启用多个账户；每个账户单独保存接口预设、令牌、刷新间隔和阈值。令牌仍按账户使用 Windows DPAPI 加密保存。
- **接口预设**：“自动识别”会在同一站点依次尝试只读的 `/v1/usage` 和 `/api/usage/token`；也可直接选择对应协议。预设模式只需填写中转站根地址和 API Key，程序会补全接口、Bearer 认证与余额字段；New API 会读取公开状态中的额度比例和 USD/CNY/Token/自定义货币设置后再显示。
- **自定义接口**：选择“自定义接口”后，可继续手动配置完整 API 地址、认证方式、请求头和 JSON 路径，旧版账户会按此模式无损迁移。
- **余额 API 地址**：中转站文档给出的余额查询 API 完整 URL，不是网站首页或聊天接口。
- **认证方式**：支持 `Bearer Token`、完整 `Authorization`、`x-api-key` 和自定义 Header。
- **余额 JSON 路径**：例如 `{ "data": { "balance": 12.3 } }` 填写 `data.balance`。
- **自动刷新间隔**：可选择关闭、30 秒、1/5/15/30 分钟、1 小时或自定义（最少 30 秒）；例如填写 `300` 表示每 5 分钟自动查询一次。关闭后不再运行后台轮询。桌宠手动刷新不受此设置影响，但两次手动刷新至少间隔 5 秒；AI 任务完成后的余额更新属于内部强制刷新。
- **语言**：可选择“简体中文”或 “English”。保存后会应用到设置窗口、桌宠菜单、气泡提示、用量统计和更新窗口。
- **识别 AI 登录账户**：启用后监听当前 Windows 用户的 `BalancePet.Account.v1` 本地命名管道。客户端只能上报登录类型、模型/服务名、可选账户标签、接口地址、令牌 SHA-256 指纹和可选余额；BalancePet 不会读取网站 Cookie、网页登录凭据或明文令牌。官方账户只显示登录状态；官方 API 可显示客户端上报的余额；已匹配本地 API 账户会自动切换并显示本地缓存余额；未匹配的第三方 API 会提示在设置中添加。
- **网络失败处理**：请求遇到超时、网络波动或 408/425/429/5xx 响应时会自动重试 2 次，仍失败则显示最近一次缓存余额（如有）。
- **设置导入/导出**：设置窗口底部可导入或导出 JSON；导出文件不会包含访问令牌，换电脑后需重新填写令牌。

桌宠本体只显示一个账户，托盘“当前账户”菜单用于切换查看对象；所有已启用账户仍会按各自间隔后台刷新。AI 任务的 `provider` 可以填写监控账户名称或 ID，以便把任务和对应账户关联；未匹配时使用当前选中账户。

令牌、接口地址和本地用量数据均不应提交到 Git。配置与令牌保存在 `%LOCALAPPDATA%\BalancePet`；令牌使用 Windows DPAPI 加密。

可参考无凭证示例：[docs/balance-pet.example.json](docs/balance-pet.example.json)。自动识别只向用户填写的同一站点发送令牌，不会把令牌交给第三方识别服务。

## 素材

桌宠状态图所使用的二次元形象参考素材均来自 Bilibili UP 主 `@ZipZipPipe`。感谢原作者的公开分享；素材的使用范围和授权条件以原作者发布页面的说明为准。

状态图位于：

```text
versions/csharp-wpf/assets/pets/<style>/<state>.png
```

支持的状态文件名：

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

素材必须是具有真实 Alpha 通道的透明 RGBA PNG。不要使用白底、灰底或棋盘格图片模拟透明；详情见 [docs/csharp-art-pipeline.md](docs/csharp-art-pipeline.md)。

## 打包

```powershell
.\tools\package-csharp-release.ps1
```

脚本会生成两种发布资产：

- `dist/BalancePet-<版本号>-Setup.exe`：推荐普通用户使用。可选择当前用户或所有用户安装、设置安装目录、创建快捷方式和卸载入口。
- `dist/BalancePet-<版本号>-win-x64.zip`：便携包，也是程序内原地更新使用的载荷。

打包安装器依赖 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。只需要本地验证 ZIP 时可传入 `-SkipInstaller`。

完整安装、升级和迁移步骤见 [docs/UPGRADE.md](docs/UPGRADE.md)。已启用的开机启动项会在新版本首次运行时更新为当前可执行文件路径。

程序内更新会按目录权限选择路径：当前用户目录或其他可写目录直接替换当前程序目录；`Program Files` 等受保护目录会下载并校验 `Setup.exe`，再由用户确认 UAC 后升级。配置、加密令牌和用量记录始终保留在 `%LOCALAPPDATA%\\BalancePet`。

`beta.7` 修复了早期预览版在部分 Windows PowerShell 环境中无法自动重启的问题。若当前正在使用 `beta.5` 或 `beta.6`，请手动覆盖安装一次 `beta.7`；之后可继续使用程序内更新。

## 互动效果

- **互动动作**：控制锁定互动时的按压、回弹、轻微倾斜和表情状态；关闭后仍可拖动桌宠、点击刷新余额。
- **随机彩蛋**：控制当前角色的专属短台词、闲置提示和连续互动彩蛋。连续快速互动四次才会触发一次彩蛋，避免频繁打扰。
- **状态切换**：正常查询成功后会短暂显示成功图，再回到待机图；鼠标按住角色期间会持续显示点击图，松开后才进入刷新或互动反馈。后台自动刷新不会重置闲置计时，15 分钟没有用户互动后会显示闲置图。
- **AI 任务状态**：启用“自动跟随 AI 任务”后，BalancePet 会监听当前用户专用的本地命名管道。Codex 继续使用内置 Hook；其他客户端或 CLI 可调用发布包中的 `tools/balancepet-task.ps1`。多个客户端可以同时上报任务；桌宠会在至少一个任务活动时保持 `codex-working`，只有全部任务完成或停止后才切换到 `codex-done`，不会被单个任务的结束事件提前覆盖。完成后会刷新余额；气泡和通知会显示客户端名称。联动只传递开始/结束、客户端名和任务 ID，不读取或保存提示词、回复或令牌。
- **无令牌保存**：余额 API 访问令牌可以暂时留空。保存设置时会跳过余额连接测试，但仍会保存 AI 任务联动等设置；之后配置令牌即可恢复余额查询。
- 气泡提示会比余额状态提示更短，余额查询、低余额和错误提示不受上述开关影响。

## AI 客户端兼容联动

BalancePet 不需要安装对应的 AI 客户端或 CLI。只要某个客户端能在任务开始和结束时执行命令，就可以调用发布包内的通用发送脚本；只支持批处理命令的客户端可以使用同目录的 `balancepet-task.cmd` 包装器。

登录状态上报使用发布包中的 `tools\balancepet-account.ps1`。例如：

```powershell
# 官方网页登录账户
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\balancepet-account.ps1" login ChatGPT official -AccountLabel "example@example.com"

# 官方 API；余额为可选的客户端已知值
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\balancepet-account.ps1" login OpenAI official-api -Endpoint "https://api.openai.com" -Balance 12.5 -Currency USD

# 第三方中转站；推荐同时传入令牌 SHA-256 指纹以匹配本地账户
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\balancepet-account.ps1" login NewAPI relay-api -Endpoint "https://relay.example.com/v1/usage" -TokenFingerprint TOKEN_FINGERPRINT_64_HEX

# 退出登录
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\balancepet-account.ps1" logout ChatGPT official
```

`TokenFingerprint` 必须是令牌本身（去掉 `Bearer ` 前缀后）的 SHA-256 十六进制结果，不能把令牌明文放进命令参数。没有指纹时，只有当接口地址恰好只对应一个本地账户时才会自动匹配；多个账户共用同一站点时不会猜测账户。客户端也可以直接向 `BalancePet.Account.v1` 发送一行 JSON，字段名与脚本参数对应。

Windows 没有可供所有 AI 客户端共用的登录状态接口，因此客户端或 Hook 必须在登录状态变化时调用上述脚本或命名管道；BalancePet 不会扫描浏览器 Cookie、客户端认证文件或进程内存。官方 API 也没有统一的余额查询协议：客户端已知余额时可通过 `-Balance` 和 `-Currency` 一并上报；匹配到本地中转站账户时，则由 BalancePet 使用该账户原有的余额配置自动刷新。

开始任务：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\balancepet-task.ps1" start <task-id> <provider>`

结束任务：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\balancepet-task.ps1" stop <task-id> <provider>`

例如客户端名可以填写 `Claude Code`、`通义灵码` 或 `generic`。`<task-id>` 应在同一任务的开始和结束事件中保持一致；停止事件没有任务 ID 时，桌宠也会在只有一个任务时自动匹配。脚本只连接本机当前用户的 `BalancePet.Task.v1` 命名管道，不开放网络端口。能够直接使用命名管道的客户端也可以发送一行 JSON（`state` 使用 `start` 或 `stop`）：`{"state":"start","sessionId":"external:<provider>","turnId":"<task-id>","provider":"<provider>"}`。未运行 BalancePet 或未勾选“自动跟随 AI 任务”时，脚本会返回错误码 2。

Gemini CLI、Qwen Code 和 Claude Code 可以配置其生命周期 Hook 调用 `tools\\balancepet-client-hook.ps1`。该适配器只从 Hook 标准输入中识别 `session_id`，将其作为任务 ID，并始终返回空 JSON；它不会读取、记录或传递提示词、回复、API 令牌或网络请求。Gemini 应使用 `BeforeAgent` / `AfterAgent`，Qwen 和 Claude 应使用 `UserPromptSubmit` / `Stop`；具体命令路径必须指向当前发布包中的该脚本。

若已安装 Gemini CLI、Qwen Code 或 Claude Code，可在发布包根目录运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\install-balancepet-client-hooks.ps1"`，自动合并当前用户的 Hook 设置；也可以将 `-Client` 指定为 `Gemini`、`Qwen` 或 `Claude`。安装器按名称去重，保留其他设置，并在修改已有设置文件前创建带时间戳的备份；客户端重启后生效。

## 项目结构

```text
versions/csharp-wpf/  C# WPF 应用
tools/                发布打包脚本
docs/                 配置示例、素材要求和许可证副本
```

## 来源与许可证

BalancePet 是独立的 C# WPF 重写项目。部分小鲸鱼素材和交互音效改编自 MIT 许可的 [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget)。原始许可证副本及完整署名见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

根目录的 [LICENSE](LICENSE) 适用于 BalancePet 的原创源代码；第三方素材继续适用其原有许可证。
