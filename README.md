# BalancePet

一只住在 Windows 桌面上的桌宠。它定时查询你配置的用量接口，把余额、花费与 AI
任务状态用一个小角色表现出来。

## 功能

**余额与花费**

轮询你配置的用量接口（官方平台或通用中转），变化时用气泡提示，并在悬停信息里
显示余额、上次余额与本次消耗。

**用量统计**

把每次查询与每笔消耗记下来，在扩展列表里查看历史与统计。

**AI 任务状态**

跟随各家客户端（Codex、Claude、Gemini、Qwen、DeepSeek 等）的任务开始与结束，
桌宠会切换到对应动作，任务进行中与完成都能看出来。

**环绕信息**

鼠标移到桌宠上并按住 `Shift`，余额、登录方式、任务状态与当前版本会环绕在它身边
显示；信息块按桌面背景自动选择浅色或深色文字。

**形象与主题**

形象与主题都是独立包，在「设置 → 扩展」里安装、更换、卸载。主程序只带一个内置
占位形象。

**其他**

账号切换、多显示器、开机自启、设置导入导出、崩溃留证。

## 安装

从 [Releases](https://github.com/GoldenMoon-cell/BalancePet/releases) 下载
`BalancePet-x.y.z-Setup.exe` 安装，或使用 `-win-x64.zip` 免安装运行。

要求 Windows 10 1809（build 17763）或更高，64 位。程序自带 .NET 运行时。

## 扩展

主程序不处理任何具体平台。功能、形象与主题都以包的形式存在，扩展在独立进程里
运行，只拿到清单里声明的那部分数据——凭据与对接口的请求始终留在主程序。

以下扩展由各自的仓库提供：

| 扩展 | 仓库 |
|---|---|
| 消息中心 | [BalancePet-Ext-Feature-NotificationCenter](https://github.com/GoldenMoon-cell/BalancePet-Ext-Feature-NotificationCenter) |
| 形象与主题 | [BalancePet-Pets](https://github.com/GoldenMoon-cell/BalancePet-Pets) |

包格式、清单字段、事件流与状态快照的规范在
[`docs/extension-spec/`](docs/extension-spec/)，第三方可据此实现自己的扩展。

## 隐私

- 访问令牌用 Windows DPAPI 加密保存，只在本机可解
- 不向任何第三方服务发送凭据
- 扩展读到的是主程序已脱敏的事件流与状态快照，不含提示词、回复与令牌

## 开发

```powershell
dotnet build versions\csharp-wpf\BalancePet.Wpf.csproj -c Release
dotnet run --project tools\pet-migration-selftest\PetMigrationSelfTest.csproj -c Release
python tools\validate-spec-documents.py
```

自测必须零失败。规范文档由校验器核对，规范与实现不一致时以规范为准。

## 许可

见 [LICENSE](LICENSE)。
