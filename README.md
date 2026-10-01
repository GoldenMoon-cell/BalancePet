# BalancePet

面向 Windows 的余额桌宠。项目当前只维护 C# WPF 版本：它会按设定间隔查询中转站提供的余额 API，并以可互动的桌宠显示状态和余额，不需要打开中转站网页。

当前正式版本：`v1.3.0`；上一正式版：`v1.2.2`。

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

- 余额接口预设：可自动识别常见接口，也可选择通用 `/v1/usage`、New API `/api/usage/token`、DeepSeek 官方平台或完整自定义配置。选择 DeepSeek 官方平台时只需填写 API Key，接口地址、JSON 路径与货币都会自动填好。
- 多账户监控：可在设置中新增多个 API/中转站账户，每个账户独立令牌、刷新间隔、缓存、用量和低余额阈值；桌宠聚合显示当前选中账户，托盘可快速切换。
- 凭证保护：令牌由 Windows DPAPI 按当前用户加密保存，不以明文写入项目配置。
- 桌宠交互：置顶显示、自由拖动、边缘吸附、锁定互动、点击刷新和状态气泡；可独立关闭互动动作或随机彩蛋。
- 通知与统计：低余额提示、每日用量和最近使用记录；New API 兼容中转站可从只读 Token 日志同步服务器实际额度，其他接口仍按上游上报显示。
- 配置迁移：可导入/导出不含令牌的设置文件，适合切换中转站或迁移到另一台电脑。
- 更新管理：可选每次启动、每天、每周或仅手动检查 GitHub Release；更新前会下载并校验 SHA-256。可写安装目录直接替换 ZIP，受保护目录会改用管理员安装器。
- 双语界面：安装器启动时可选择简体中文或 English；桌宠设置中也可随时切换应用语言。
- 托盘驻留与开机启动：支持从托盘配置、刷新、查看统计和退出；右键菜单的“切换形象”可快速切换当前已完成的澜汐、霁珑、绯音、星璃、烬斧、丹笺、虹谱、绀华、青绡、青棱、玄珏和绒眠。
- AI 联动：设置窗口有独立的“AI 联动”页。DeepSeek Harness、Codex、Gemini CLI、Qwen Code、Claude Code 各有一个开关，另加一个“其他客户端”兜底，未列出的自定义 CLI 不会失效。开关打开时自动写入该客户端需要的 Hook，关闭时自动移除；未安装的客户端会显示为警示色并附小字说明，仍然可以开启，等它出现后自动补写配置。
- 费用归属：用量记录会带上实际扣费的账户，用量统计中显示为“客户端 · 账户名”。中转站账户抓取逐次计费日志并标注“消耗额度”；官方 API 账户改用任务前后余额差值给出“任务总消耗”，并注明是余额变化而非逐次计费。两条路都有数据时以逐次日志为准。
- 状态素材：当前支持 DeepSeek 小鲸鱼「澜汐」、ChatGPT 小白龙「霁珑」、MiniMax 小海螺「绯音」、Gemini 小星猫「星璃」、Grok 小恶魔「烬斧」、Claude 小书灵「丹笺」、Kimi 小棱镜「虹谱」、Qwen 小折扇「绀华」、Ernie 小病书灵「青绡」、GLM 小方灵「青棱」、GPT Image 2 小墨龙「玄珏」和 Llama 小羊驼「绒眠」的九种状态图；其他已登记形象会在素材完整前保留在目录中，不出现在选择菜单。

## 扩展

`v0.5.0` 开始提供资源型宠物扩展基础；`v0.6.0` 增加功能扩展的独立进程宿主和脱敏用量事件管道。当前插件库提供 Usage Analytics `v0.3.10`、Notification Center `v0.5.15`、Mica 主题 `v1.0.0` 和 Browser Bridge `v1.0.0`。打开“配置接口”窗口的“扩展”区域，程序会先显示在线插件库，再扫描主程序目录旁 `extension-library` 文件夹中的顶层 `.zip` 包。功能、资源和主题扩展可以从在线目录下载并经过本地校验后安装；浏览器扩展会明确标记为“浏览器扩展”，只提供打开独立仓库的按钮，因为它不能由主程序直接安装或加载。目录不可用时会显示最近一次有效缓存，仍可导入本地 ZIP。扩展条目右侧提供图标化的安装/卸载、启用/禁用、更新和启动操作；同一扩展的不同版本会合并为一个条目。扩展更新可单独选择每次启动、每天、每周或仅手动检查；更新地址由扩展 manifest 的 `update_url` 声明，下载后仍会经过正常的 ZIP 和 manifest 校验。卸载只删除 `%LOCALAPPDATA%\BalancePet\extensions` 中的运行副本，不删除扩展库里的 ZIP，因此之后可以重新安装。功能扩展不会被加载进主程序进程，只能通过声明的能力读取本机脱敏数据。插件库规范见 [docs/extension-spec/feature-v1/README.md](docs/extension-spec/feature-v1/README.md)。

资源扩展包格式见 [docs/extension-spec/v1/README.md](docs/extension-spec/v1/README.md)，功能扩展协议见 [docs/extension-spec/feature-v1/README.md](docs/extension-spec/feature-v1/README.md)，声明式主题协议见 [docs/extension-spec/theme-v1/README.md](docs/extension-spec/theme-v1/README.md)。主题扩展只包含经过白名单校验的颜色、圆角和材质令牌，不加载任意 XAML 或代码；Windows 云母效果始终由主程序调用系统接口。可用 [tools/package-pet-extension.ps1](tools/package-pet-extension.ps1) 打包九状态宠物形象，用 [tools/package-theme-extension.ps1](tools/package-theme-extension.ps1) 打包主题。用量统计插件位于 `extensions/BalancePet-Ext-Feature-UsageAnalytics/`，通过右键菜单“用量统计”启动；它会监听事件文件变化，并以不超过 60 秒的间隔自动刷新。任务完成事件可以自动记录请求次数和耗时；要统计真实 Token、缓存命中和首 Token 延迟（TTFT），客户端还需主动上报 Usage.v1 字段。主程序还会把基础窗口使用的本地余额变化账本导出为脱敏的 `balance-usage.v1.json`，供插件显示“今日消费”；这代表余额下降估算，不是中转站账单。主程序只接收计数和耗时等元数据，不接收提示词、回复或令牌。功能扩展协议只规定 manifest、`--data-dir`、Usage Event v1、Balance Usage v1、能力和生命周期；第三方扩展可以自由选择 UI 工具包、主题、Logo、窗口布局和交互方式。

若中转站的费用明细接口需要网页会话，可从独立的 [BalancePet Browser Bridge 仓库](https://github.com/GoldenMoon-cell/BalancePet-BrowserBridge) 获取 `v1.0.0` 扩展。主程序发布包也会附带同一扩展 ZIP 作为便利下载。在 Edge 或 Chrome 的扩展管理页开启开发人员模式并加载解压后的扩展目录，然后在 BalancePet 设置的“高级：从本机浏览器读取会话”中生成配对码，在已登录中转站的标签页点击扩展并同步即可。Cookie 仅通过本机回环地址传给 BalancePet，并使用 DPAPI 加密保存；不需要手动复制 Cookie。

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
- **接口预设**：“自动识别”会在同一站点依次尝试只读的 `/v1/usage` 和 `/api/usage/token`；也可直接选择对应协议。预设模式只需填写中转站根地址和 API Key，程序会补全接口、Bearer 认证与余额字段；New API 会读取公开状态中的额度比例和 USD/CNY/Token/自定义货币设置后再显示，并尝试从同站点的只读 `/api/log/token` 同步逐次消费。
- 对只提供 `/v1/usage` 的站点，主程序会把 `daily_usage.actual_cost`（没有时使用 `cost`）作为脱敏的按日汇总写给用量统计插件；这不是单条请求费用，无法替代中转站请求日志。
- **自定义接口**：选择“自定义接口”后，可继续手动配置完整 API 地址、认证方式、请求头和 JSON 路径，旧版账户会按此模式无损迁移。
- **余额 API 地址**：中转站文档给出的余额查询 API 完整 URL，不是网站首页或聊天接口。
- **认证方式**：支持 `Bearer Token`、完整 `Authorization`、`x-api-key` 和自定义 Header。
- **余额 JSON 路径**：例如 `{ "data": { "balance": 12.3 } }` 填写 `data.balance`。
- **自动刷新间隔**：可选择关闭、30 秒、1/5/15/30 分钟、1 小时或自定义（最少 30 秒）；例如填写 `300` 表示每 5 分钟自动查询一次。关闭后不再运行后台轮询。桌宠手动刷新不受此设置影响，但两次手动刷新至少间隔 5 秒；AI 任务完成后的余额更新属于内部强制刷新。
- **语言**：可选择“简体中文”或 “English”。保存后会应用到设置窗口、桌宠菜单、气泡提示、用量统计和更新窗口。
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
- **随机彩蛋**：控制当前角色的专属短台词、闲置提示和连续互动彩蛋。不同角色、触摸部位和连续互动会从独立候选池取词，并避开最近使用的台词；连续快速互动四至六次才会触发一次彩蛋，避免频繁打扰。
- **状态切换**：正常查询成功后会短暂显示成功图，再回到待机图；鼠标按住角色期间会持续显示点击图，松开后才进入刷新或互动反馈。后台自动刷新不会重置闲置计时，15 分钟没有用户互动后会显示闲置图。
- **AI 任务状态**：在设置窗口的“AI 联动”页里分别开关。BalancePet 会监听当前用户专用的本地命名管道，桌宠在至少一个任务活动时保持 `codex-working`，全部任务完成或停止后才切换到 `codex-done`，不会被单个任务的结束事件提前覆盖。完成后会刷新余额；气泡会显示客户端名称。联动只传递开始/结束、客户端名和任务 ID，不读取或保存提示词、回复或令牌。
- **按客户端开关**：每个客户端一个独立开关 —— Codex、DeepSeek Harness、Gemini CLI、Qwen Code、Claude Code，以及“其他客户端”。开关打开时 BalancePet 会自动写入该客户端需要的 Hook（Codex 用 `~/.codex/hooks.json`，Gemini/Qwen/Claude 用各自的 `settings.json`，DeepSeek Harness 用 Cordis 插件）；关闭时自动移除。未安装的客户端开关会显示为警示色并附小字说明，仍然可以打开：勾选只记录意图，等该客户端出现后下一次启动会自动补写配置，不会为没装的客户端凭空创建配置文件。自定义 CLI 只要调用 `tools/balancepet-task.ps1`，就会被“其他客户端”接管，所以未列出的集成不会失效。
- **DeepSeek Harness 联动**：DeepSeek Harness 没有 Codex 那样的 hooks.json，它的官方扩展点是 Cordis 插件，所以 BalancePet 附带一个桥接插件（`tools/dsh-bridge/`）。正常不需要手动安装 —— 在“AI 联动”页勾选 DeepSeek Harness 即可，保存时它会自动挂载。也可以手动运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\install-balancepet-dsh-plugin.ps1"`。DSH 会自动重新加载 profile，但**只限首次安装**：替换磁盘上已有的插件文件后必须重启 DSH，因为 ESM 模块在进程内按 URL 缓存。诊断日志写在 `%LOCALAPPDATA%\BalancePet\dsh-bridge.log`，出现 `bridge active` 即表示插件已生效。插件订阅官方的 `turn/start` 与 `turn/end` 会话事件，通过同一个本地命名管道上报，因此气泡和余额刷新行为与其他客户端一致。除了开始与结束，它还会上报**本回合增量**的模型、思考强度、输入/输出/缓存读写 token、步数和耗时，所以用量统计里 DeepSeek Harness 的记录与 Codex 一样有明细。插件不上报费用：DSH 只统计 token、没有价格表，走中转站时由主程序的逐条日志回填，走官方账户时显示“未上报”。安装只是把插件目录复制进 profile 并在 `cordis.patch.yml` 追加一条挂载项，不改动 profile 的 `package.json`，也不联网；卸载传 `-Action Uninstall`，两次操作都会先备份该配置文件。
- **用量归属与费用来源**：每条用量记录会带上它实际扣费的账户，用量统计里显示为「客户端 · 账户名」。费用按账户类型分两种口径：中转站账户能抓到逐次计费日志，记录每次请求的额度（标为「消耗额度」）；官方 API 账户没有逐次计费接口，改用**任务前后余额差值**给出该次任务的总消耗（标为「任务总消耗」，并注明是余额变化而非逐次计费）。两条路都有数据时以逐次日志为准。余额差值只在账户不是中转站时启用，判断依据是现成的中转站能力探测，不需要额外配置。要让它生效，需要把官方账户本身加进监控，例如 DeepSeek：接口地址 `https://api.deepseek.com/user/balance`、认证方式 `authorization`、余额路径 `balance_infos.0.total_balance`、币种路径 `balance_infos.0.currency`。注意余额至少 30 秒才刷新一次，短于该间隔的任务可能测不到消耗。
- **无令牌保存**：余额 API 访问令牌可以暂时留空。保存设置时会跳过余额连接测试，但仍会保存 AI 任务联动等设置；之后配置令牌即可恢复余额查询。
- 气泡提示会比余额状态提示更短，余额查询、低余额和错误提示不受上述开关影响。

## 自定义客户端接入

BalancePet 不需要安装对应的 AI 客户端或 CLI。未在上面列出的自定义客户端，只要按下面的方式上报开始和结束，就会被“其他客户端”接管。

开始任务：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\balancepet-task.ps1" start <task-id> <provider>`

结束任务：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\balancepet-task.ps1" stop <task-id> <provider>`

例如客户端名可以填写 `Claude Code`、`通义灵码` 或 `generic`。`<task-id>` 应在同一任务的开始和结束事件中保持一致；停止事件没有任务 ID 时，桌宠也会在只有一个任务时自动匹配。脚本只连接本机当前用户的 `BalancePet.Task.v1` 命名管道，不开放网络端口。能够直接使用命名管道的客户端也可以发送一行 JSON（`state` 使用 `start` 或 `stop`）：`{"state":"start","sessionId":"external:<provider>","turnId":"<task-id>","provider":"<provider>"}`。未运行 BalancePet 或未勾选“自动跟随 AI 任务”时，脚本会返回错误码 2。

Gemini CLI、Qwen Code 和 Claude Code 可以配置其生命周期 Hook 调用 `tools\\balancepet-client-hook.ps1`。该适配器只从 Hook 标准输入中识别 `session_id`，将其作为任务 ID，并始终返回空 JSON；它不会读取、记录或传递提示词、回复、API 令牌或网络请求。Gemini 应使用 `BeforeAgent` / `AfterAgent`，Qwen 和 Claude 应使用 `UserPromptSubmit` / `Stop`；具体命令路径必须指向当前发布包中的该脚本。

若已安装 Gemini CLI、Qwen Code 或 Claude Code，可在发布包根目录运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\install-balancepet-client-hooks.ps1"`，自动合并当前用户的 Hook 设置；也可以将 `-Client` 指定为 `Gemini`、`Qwen` 或 `Claude`。安装器按名称去重，保留其他设置，并在修改已有设置文件前创建带时间戳的备份；客户端重启后生效。

## 项目结构

```text
versions/csharp-wpf/  C# WPF 应用
tools/                发布打包脚本
docs/                 配置示例、素材要求、扩展规范和许可证副本
```

## 来源与许可证

BalancePet 是独立的 C# WPF 重写项目。部分小鲸鱼素材和交互音效改编自 MIT 许可的 [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget)。原始许可证副本及完整署名见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

根目录的 [LICENSE](LICENSE) 适用于 BalancePet 的原创源代码；第三方素材继续适用其原有许可证。
