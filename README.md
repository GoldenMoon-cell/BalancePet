# BalancePet

[![下载量](https://img.shields.io/github/downloads/GoldenMoon-cell/BalancePet/total?label=%E4%B8%8B%E8%BD%BD%E9%87%8F&color=2ea043)](https://github.com/GoldenMoon-cell/BalancePet/releases)
[![最新版本](https://img.shields.io/github/v/release/=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&color=2ea043)](https://github.com/GoldenMoon-cell/BalancePet/releases/latest)
[![Stars](https://img.shields.io/github/stars/=Stars&color=2ea043)](https://github.com/GoldenMoon-cell/BalancePet/stargazers)

一只住在 Windows 桌面上的桌宠。它定时查询你配置的用量接口，把余额、花费与 AI
任务状态用一个小角色表现出来。


<p align="center">
  <img src="https://github-readme-stats.vercel.app/api?username=GoldenMoon-cell&show_icons=true&locale=cn" alt="GitHub 统计" height="150" />
  <img src="https://github-readme-stats.vercel.app/api/top-langs/?username=GoldenMoon-cell&layout=compact&locale=cn" alt="热门语言" height="150" />
</p>
## 功能

- **余额与花费** —— 轮询你配置的用量接口（官方平台或通用中转），变化时用气泡提示
- **AI 任务状态** —— 跟随各客户端（Codex、Claude、Gemini、Qwen、DeepSeek 等）的任务开始与结束，桌宠切换到对应动作
- **环绕信息** —— 鼠标移到桌宠上并按住 `Shift`，余额、登录方式、任务状态与当前版本环绕显示
- **更新记录** —— 抓取在线更新记录，有新内容时桌宠提一句；内容交给「消息中心」扩展展示
- **扩展管理** —— 在「设置 → 扩展」里安装、更换、卸载形象与功能扩展
- **其他** —— 账号切换、多显示器、开机自启、设置导入导出、崩溃留证

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


