# BalancePet

一只住在 Windows 桌面上的桌宠。它会定时查询你配置的用量接口，把余额、花费与
AI 任务状态用一个小角色表现出来；悬停并按住 `Shift` 可以看到环绕在它身边的
实时信息。

形象不是程序的一部分。主程序只带一个内置占位形象，其他形象都以独立包发布，
在「设置 → 扩展」里安装或更换——美术是安装包和每次更新里最大的部分，把它挪出
主程序之后，主程序的安装包与更新都保持在几十兆。

## 功能

| | |
|---|---|
| **余额与花费** | 轮询你配置的用量接口，变化时会用气泡提示 |
| **AI 任务状态** | 跟随各家客户端的任务开始/结束，桌宠会切换动作 |
| **环绕信息** | 悬停按住 `Shift`，余额、登录方式、任务状态与版本环绕显示 |
| **消息中心** | 独立扩展，集中显示更新记录、任务、账户、余额与系统消息，未读按分类标记 |
| **更新记录** | 由主程序抓取、交给消息中心呈现，有新内容时桌宠会提一句 |
| **形象** | 18 套公开形象，可在扩展列表里安装、更换、卸载 |
| **主题** | 独立主题扩展；扩展窗口会跟随主程序的主题与字体 |

## 安装

从 [Releases](https://github.com/GoldenMoon-cell/BalancePet/releases) 下载
`BalancePet-x.y.z-Setup.exe` 安装，或使用 `-win-x64.zip` 免安装运行。

要求 Windows 10 1809（build 17763）或更高，64 位。程序自带 .NET 运行时，
不需要另外安装。

## 形象

形象与主题通过仓库分发，源站在
[BalancePet-Pets](https://github.com/GoldenMoon-cell/BalancePet-Pets)：

- 每套形象包含九张状态图、一句介绍与一组台词（彩蛋）
- 形象包在「设置 → 扩展」里安装，安装后可在「桌宠与交互」里切换
- 扩展列表里的图标按需下载并缓存在本机，只下一次；缓存占用与清除在
  「设置 → 高级与迁移」

## 扩展

主程序本身不处理具体平台。功能、形象、主题都以包的形式存在，由扩展在独立进程
里运行，只拿到清单里声明的那部分数据——凭据与对接口的请求始终留在主程序。

规范见 [`docs/extension-spec/`](docs/extension-spec/)：包格式、清单字段、事件流与
状态快照都在那里写明，第三方可以据此实现自己的扩展。

## 隐私

- 访问令牌用 Windows DPAPI 加密保存，只在本机可解
- 不向任何第三方服务发送凭据
- 消息中心等扩展读的是主程序已脱敏的事件流与状态快照，其中不含提示词、回复或令牌

## 开发

```powershell
dotnet build versions\csharp-wpf\BalancePet.Wpf.csproj -c Release
dotnet run --project tools\pet-migration-selftest\PetMigrationSelfTest.csproj -c Release
python tools\validate-spec-documents.py
```

自测必须零失败。规范文档由校验器核对——规范与实现不一致时，以规范为准。

## 许可

见 [LICENSE](LICENSE)。
