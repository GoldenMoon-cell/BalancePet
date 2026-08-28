# 小余额：第三方中转站余额桌宠

当前公开预览版本：`v0.1.0-beta.1`

原始 BalancePet 代码采用 MIT 许可证；随附素材和其他第三方内容请同时遵守 `THIRD_PARTY_NOTICES.md` 中的说明。

桌面工作区现在按版本分开维护，C# WPF 是唯一继续开发的版本：

- `versions/csharp-wpf`：当前主版本，后续功能、修复和打包都在这里进行。
- `versions/python`：历史版本，仅保留作为功能和故障排查参考。
- `versions/electron`：历史版本，仅保留作为界面和交互参考。
- `archive`：历史构建产物和旧资源，仅用于回溯。

## 默认运行

```powershell
.\launch-balance-pet.bat
```

C# 版本的令牌使用 Windows DPAPI 加密保存，余额接口不经过 Chromium。也可以直接运行 `launch-balance-pet-csharp.bat`。

视觉与部分交互参考了 MIT 许可的 [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget)，详情见 `THIRD_PARTY_NOTICES.md`。

---

这是一个 Windows 原生桌宠：运行后会显示一个可拖动、置顶的小鲸鱼，并按间隔请求你配置的余额 API。它不需要每次打开中转站网页。

## 运行

需要 Windows 和 .NET 8 Desktop Runtime（开发机安装 .NET 8 SDK 时可直接编译）。双击根目录的 `launch-balance-pet.bat` 可启动 C# 版本；Python 和 Electron 入口仅用于历史版本复现。

```powershell
dotnet build .\versions\csharp-wpf\BalancePet.Wpf.csproj --configuration Release
```

首次启动会打开配置窗口；之后右键宠物选择“配置接口”。点击宠物刷新并显示余额气泡，点击气泡可切换台词；拖动到屏幕边缘会自动吸附，左侧吸附会镜像翻转。

右键菜单或设置窗口可以切换交互模式：`自由拖动` 模式下按住宠物可移动并吸附到屏幕边缘；`锁定互动` 模式下窗口位置固定，按住头顶可提呆毛，按住脸部可拽嘴角，点击身体仍会刷新余额。当前这些局部互动使用程序化回弹，换成分层或骨骼素材后可继续增强。

## 动作素材扩展

程序已经按状态驱动动画。没有专用动作图时，会自动回退到基础形象；C# 版本可为某个形象添加状态 PNG：

```text
versions/csharp-wpf/assets/pets/<style>/<state>.png
```

例如 `versions/csharp-wpf/assets/pets/chatgpt/loading.png`、`versions/csharp-wpf/assets/pets/deepseek/error.png`。支持 `idle`、`loading`、`success`、`low`、`error`、`clicked`、`codex-working`、`codex-done` 和 `inactive`。只要图片使用透明背景、保持同一画布尺寸和角色锚点，放入目录后无需改代码；缺少某个状态图时会自动使用该形象的基础图。

你可以使用 APIMart 的 `gpt-image-2` 生成图片，再把图片放在桌面或 Downloads，告诉我每张图对应的形象和状态。我会负责检查透明边缘、统一尺寸、复制到目录并接入；不需要把 APIMart 密钥交给程序。完整提示词和交付规则见 `docs/csharp-art-pipeline.md`。推荐 PNG，画布 1024x1024 或 2048x2048，角色脚底和身体中心在所有状态中保持一致；不要使用纯色背景或截图中的桌面背景。如果暂时只有一张静态图，也可以先作为 `idle.png`，其余状态会回退到基础图。

## 配置字段

- 余额 API 地址：中转站提供的余额查询接口 URL。
- 认证方式：`bearer` 会发送 `Authorization: Bearer <token>`；`authorization` 直接发送 `Authorization: <token>`；`x-api-key` 发送 `x-api-key: <token>`。
- 令牌：保存时使用 Windows DPAPI 按当前用户加密，不会以明文写入配置。
- 余额 JSON 路径：例如响应 `{ "data": { "balance": 12.3 } }` 就填 `data.balance`。
- 刷新间隔：最小 30 秒，避免频繁打 API。
- 低余额提醒：余额小于等于这个数时，宠物状态变为“余额偏低”。

配置会保存到程序旁边的 `balance-pet.json`。不要把这个文件提交到公共仓库。

## 中转站适配

不同中转站的接口地址、鉴权名称和返回 JSON 结构不统一。配置时请以中转站的 API 文档为准。

### 1. 找到余额查询接口

在文档中搜索“余额查询”“账户信息”“个人信息”“quota”“credits”或“remaining”。这里要填余额查询 API 的完整 URL，不是中转站首页，也不是聊天接口（例如 `/v1/chat/completions`）。当前版本发送 `GET` 请求，例如：

```text
https://relay.example.com/api/user/balance
```

如果文档要求查询参数，也要保留在 URL 中。若站点只提供 `POST` 余额接口或只提供网页显示余额，当前通用适配器不能直接调用，需要单独开发适配器。

### 2. 选择认证方式

设置窗口中的认证方式对应以下请求头：

| 认证方式 | 填写内容 | 程序发送的请求头 |
| --- | --- | --- |
| `Bearer Token` | 只填令牌 | `Authorization: Bearer <令牌>` |
| `完整 Authorization` | 通常填 `Bearer <令牌>` | `Authorization: <填写内容>` |
| `x-api-key` | 只填令牌 | `x-api-key: <令牌>` |
| `自定义 Header` | 填令牌，并填写 Header 名 | `<Header 名>: <令牌>` |
| `中转站会话（websee-session）` | 按该站点说明填写 | Bearer 认证及该站点需要的会话请求头 |

不要把令牌写进 README、示例配置、截图或提交到 Git。C# 版本会使用 Windows DPAPI 在本机保存令牌。

### 3. 填写余额 JSON 路径

打开文档中的响应示例，找到代表剩余余额的数字或数字字符串，用点号写出路径：

```json
{ "balance": 12.5 }
```

```text
balance
```

```json
{ "data": { "quota": { "remaining": "23.80" } } }
```

```text
data.quota.remaining
```

数组使用数字下标，例如 `{ "data": [{ "balance": 12.5 }] }` 对应：

```text
data.0.balance
```

`货币`填写 `USD`、`CNY` 等；刷新秒数必须为 30 或更大。

### 4. 根据错误定位问题

- `401` 或 `403`：认证方式或令牌不正确。
- `404`：余额接口 URL 不正确。
- `JSON path not found`：JSON 路径没有指向余额字段。
- 返回 HTML：填成了网站页面地址，而不是 API 地址。

完整的占位配置可参考 [`docs/balance-pet.example.json`](docs/balance-pet.example.json)。

若中转站只提供网页而没有文档化的余额 API，本项目不会模拟登录或抓取网页，以避免泄露登录态和触发风控。

桌宠使用参考项目在 MIT 许可下提供的小鲸鱼素材，保留来源与许可证说明。它是独立余额桌宠，不会改变 Codex 内置宠物的任务状态。
