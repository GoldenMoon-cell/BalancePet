# BalancePet

面向 Windows 的余额桌宠。项目当前只维护 C# WPF 版本：它会按设定间隔查询中转站提供的余额 API，并以可互动的桌宠显示状态和余额，不需要打开中转站网页。

当前预览版本：`v0.1.0-beta.9`。

## 功能

- 通用余额接口：可配置 API 地址、认证方式、请求头和余额 JSON 路径。
- 凭证保护：令牌由 Windows DPAPI 按当前用户加密保存，不以明文写入项目配置。
- 桌宠交互：置顶显示、自由拖动、边缘吸附、锁定互动、点击刷新和状态气泡；可独立关闭互动动作或随机彩蛋。
- 通知与统计：低余额提示、系统通知、每日用量和最近使用记录。
- 配置迁移：可导入/导出不含令牌的设置文件，适合切换中转站或迁移到另一台电脑。
- 更新管理：可选每次启动、每天、每周或仅手动检查 GitHub Release；确认后自动下载、SHA-256 校验、静默替换并重启更新，重启后会显示更新完成版本。
- 托盘驻留与开机启动：支持从托盘配置、刷新、查看统计和退出；可自动跟随 Codex 任务的开始和结束。
- 状态素材：支持 DeepSeek 小鲸鱼「澜汐」与 GPT 小龙「霁珑」的待机、加载、成功、低余额、错误、点击、Codex 工作/完成和闲置状态图。

## 运行

需要 Windows 与 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

开发环境构建：

```powershell
dotnet build .\versions\csharp-wpf\BalancePet.Wpf.csproj --configuration Release
```

构建后运行根目录的 `launch-balance-pet.bat`，或直接启动生成的 `BalancePet.Wpf.exe`。首次启动会打开配置窗口；之后可从托盘菜单选择“配置接口”。

## 配置接口

- **余额 API 地址**：中转站文档给出的余额查询 API 完整 URL，不是网站首页或聊天接口。
- **认证方式**：支持 `Bearer Token`、完整 `Authorization`、`x-api-key` 和自定义 Header。
- **余额 JSON 路径**：例如 `{ "data": { "balance": 12.3 } }` 填写 `data.balance`。
- **刷新间隔**：最小为 30 秒。
- **网络失败处理**：请求遇到超时、网络波动或 408/425/429/5xx 响应时会自动重试 2 次，仍失败则显示最近一次缓存余额（如有）。
- **设置导入/导出**：设置窗口底部可导入或导出 JSON；导出文件不会包含访问令牌，换电脑后需重新填写令牌。

令牌、接口地址和本地用量数据均不应提交到 Git。配置与令牌保存在 `%LOCALAPPDATA%\BalancePet`；令牌使用 Windows DPAPI 加密。

可参考无凭证示例：[docs/balance-pet.example.json](docs/balance-pet.example.json)。

## 素材

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

脚本会生成 `dist/BalancePet-<版本号>-win-x64.zip`，其中包含程序、素材、许可证和第三方声明。

从旧版本升级时请先从托盘退出旧程序，再解压并启动新版本一次；完整步骤见 [docs/UPGRADE.md](docs/UPGRADE.md)。已启用的开机启动项会自动迁移到新版本路径。

程序内更新只替换当前程序目录中的文件，配置、加密令牌和用量记录仍保留在 `%LOCALAPPDATA%\\BalancePet`。

`beta.7` 修复了早期预览版在部分 Windows PowerShell 环境中无法自动重启的问题。若当前正在使用 `beta.5` 或 `beta.6`，请手动覆盖安装一次 `beta.7`；之后可继续使用程序内更新。

## 互动效果

- **互动动作**：控制锁定互动时的按压、回弹、轻微倾斜和表情状态；关闭后仍可拖动桌宠、点击刷新余额。
- **随机彩蛋**：控制澜汐/霁珑的角色专属短台词、闲置提示和连续互动彩蛋。连续快速互动四次才会触发一次彩蛋，避免频繁打扰。
- **状态切换**：正常查询成功后会短暂显示成功图，再回到待机图；点击时会先显示点击图约 0.4 秒。后台自动刷新不会重置闲置计时，15 分钟没有用户互动后会显示闲置图。
- **Codex 状态**：启用“自动跟随 Codex 任务”后，BalancePet 会安装一个需要 Codex 审核信任的本地 Hook。按发送箭头时，桌宠切换到 `codex-working`；任务完成或手动停止时，切换到 `codex-done` 并刷新余额。Hook 只传递开始/停止事件和任务 ID，不读取或保存提示词、回复或令牌。
- 气泡提示会比余额状态提示更短，余额查询、低余额和错误提示不受上述开关影响。

## 项目结构

```text
versions/csharp-wpf/  C# WPF 应用
tools/                发布打包脚本
docs/                 配置示例、素材要求和许可证副本
```

## 来源与许可证

BalancePet 是独立的 C# WPF 重写项目。部分小鲸鱼素材和交互音效改编自 MIT 许可的 [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget)。原始许可证副本及完整署名见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

根目录的 [LICENSE](LICENSE) 适用于 BalancePet 的原创源代码；第三方素材继续适用其原有许可证。
