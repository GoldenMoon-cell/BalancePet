# BalancePet 工作状态与协作规范

> 这是一份用于迁移和交接的工作记录。每完成一次版本开发、打包或发布，都要更新“当前状态”和“版本记录”。
>
> 最后更新：2026-09-13

## 当前状态

- 项目：BalancePet，Windows 桌面宠物，用于轮询用户配置的中转站余额接口并显示结果。
- 当前仓库可见并持续构建的实现：`versions/csharp-wpf`。Python 版本保留为 fallback，不主动删除；如果后续恢复或新增 Electron 实现，继续遵守根目录 `AGENTS.md` 中关于 Electron 主进程凭据隔离的约束。
- 发布候选：主程序 `v0.7.8`，功能扩展 `Usage Analytics v0.2.9`；上一版已发布版本为 `v0.5.0`。
- 工作区：主程序 `v0.7.8` 与 `BalancePet-Ext-Feature-UsageAnalytics v0.2.9` 已完成规范整理、构建和打包，准备发布。
- 最近验证：主程序和用量统计插件的 `dotnet build` 均通过，0 警告、0 错误；主程序 Inno Setup 6.7.3 打包成功；`v0.7.6` 安装器与便携 ZIP、统计扩展 0.2.4 ZIP 均已生成；脱敏 Usage Event v1 写入脚本已通过字段校验；设置窗口保持紧凑的 640×720 分栏布局，并加入浅色主题、统一控件边框和卡片容器；扩展页现可将同一 ID 的多个版本合并为一个条目，行内提供图标化安装/卸载、启用/禁用、更新和启动操作，并支持独立的扩展更新检查频率；扩展清单可声明 GitHub `update_url`，下载后仍经过 ZIP 与 manifest 校验；用量统计插件的“仪表盘/使用记录/提供方与模型”侧边导航已接通，生命周期-only 数据会明确提示客户端尚未上报 Token、模型和耗时；统计窗口使用无边框内置标题栏、深色滚动条和实时倒计时；Codex Hook、通用 Task.v1 和 Usage.v1 解析支持更多嵌套用量字段。
- `v0.3.3` 是 GitHub 已发布版本。此前为测试新预设而生成的同名本地包仍在 `dist/`，但不应再作为发布物使用：`BalancePet-0.3.3-win-x64.zip`（200,236,072 bytes，SHA-256 `c5d4f105169172a320b62dc81cb06ae39962068f0f40fbbae61f2586adba30cb`）；`BalancePet-0.3.3-Setup.exe`（180,300,513 bytes，SHA-256 `c1ed3088885087d165344f6947f7efefecec77f30bbfc776a7eedd94a291ad5a`）。
- 当前已发布版本为 `0.5.0`；`0.7.8` 主程序与 `Usage Analytics 0.2.9` 功能扩展已完成构建，发布说明和规范检查，等待推送与 Release 创建。
- `0.4.0` 发布资产：`BalancePet-0.4.0-win-x64.zip` SHA-256 `2a677017695b353f6b11be0fc1f8e3d387f6db87c4ce8815398f501ba409be68`；`BalancePet-0.4.0-Setup.exe` SHA-256 `d8fe4651146f8537c838a049dcebd16a76776124e8017c3bfd5616be02e3bba0`。
- `0.5.0` GitHub 发布包（含分栏设置界面、完整页签英文本地化、普通保存后语言刷新、认证提示翻译修复、菜单窗口崩溃修复、语言选项显示修复和资源型扩展基础）：`BalancePet-0.5.0-win-x64.zip`（202,447,550 bytes，SHA-256 `34c7fbed86a08e45fcfae619ed36a0a4bc5960c75b9b723806b9e161f0eb3d06`）；`BalancePet-0.5.0-Setup.exe`（180,318,110 bytes，SHA-256 `df5f270a2ce6c6664386c0c34653a258ff6812d04650793a7639730e26204732`）。
- `0.7.0` 本地测试包（扩展版本合并、行内图标操作、扩展独立更新检查，以及设置窗口模态关闭修复）：`BalancePet-0.7.0-win-x64.zip`（200,284,236 bytes，SHA-256 `06066D03AE8A6A717B1F6139E773D45743740AFDDAF7460390DF6DC89B90285D`）；`BalancePet-0.7.0-Setup.exe`（180,338,402 bytes，SHA-256 `50E9B9B95836A6813476CF54A77188C314224BA2CE24792B394A5DBC40AD2942`）。尚未提交或发布。
- `0.7.1` 本地测试包（下拉框 Tag 选择回显修复、更新频率和语言/形象/交互持久化修复、Hook 用量字段兼容、插件导航可靠性和无计数数据提示）：`BalancePet-0.7.1-win-x64.zip`（SHA-256 `3495C313ED22168ECFBE7B75E6A1A943800D07042E165DD482A6970887A0A76C`）；`BalancePet-0.7.1-Setup.exe`（SHA-256 `7080389F5AF54D0B2EDDEFD2B203E660958651736FB2648FA437D175C33CA9AC`）。尚未提交或发布。
- `0.7.2` 本地测试包（普通“保存设置”与“保存并测试”统一关闭窗口并立即让主窗口重新加载设置）：`BalancePet-0.7.2-win-x64.zip`（202,478,715 bytes，SHA-256 `4594C2CCF092FF3F7B21780B6E7C384568A4AB49922262AB888AD297593A5450`）；`BalancePet-0.7.2-Setup.exe`（180,335,158 bytes，SHA-256 `580CA1B1A0DAEB254B46E24388D7CB26DA74EC9056E07AC9E64DFA24DC6A83F3`）。尚未提交或发布。
- `0.7.3` 本地测试包（Windows 风格“取消/确定/应用”按钮；“应用”保存后通过事件立即刷新主窗口，设置窗口保持打开）：`BalancePet-0.7.3-win-x64.zip`（SHA-256 `5C005546CA897AEA6ED40D37D959A7697B3EA10852F764516204EBA036BB4EEA`）；`BalancePet-0.7.3-Setup.exe`（SHA-256 `F1BCE97C7CC7390046B794BEFE5EE317528673F607700DDB4D0CB1AE7064124B`）。尚未提交或发布。
- `0.7.4` 本地测试包（恢复 0.4.0 的 ComboBox 直接选择方式，移除 SelectedValuePath 对显示层的影响）：`BalancePet-0.7.4-win-x64.zip`（202,479,194 bytes，SHA-256 `E0DE3450BD8EE9739AF9262879CFB06796F2DDAD13B47A454D9F6A70846B2132`）；`BalancePet-0.7.4-Setup.exe`（180,342,774 bytes，SHA-256 `96D0CD71EC1ED311EE7C463DFCB7455FE874A29AA34A4D46F79FEF6B7C2ACBC9`）。尚未提交或发布。
- `0.7.5` 本地测试包（所有设置下拉框使用只读可编辑显示，选择变化和下拉关闭后立即同步显示文本；补充初始化、语言切换和标签页切换后的统一同步）：`BalancePet-0.7.5-win-x64.zip`（202,479,587 bytes，SHA-256 `A61793D0E0085A5F5D49A3B423A9DB048C75211BC52708728634BF425086782C`）；`BalancePet-0.7.5-Setup.exe`（180,341,166 bytes，SHA-256 `2CA68A290AFFC5DCF24AC80EB36AA02CBA3C45207671E639680198CB9CA70766`）。尚未提交或发布。
- `0.7.6` 本地测试包（扩展用量字段兼容、统计窗口无边框融合标题栏和深色滚动条、自动刷新倒计时、Start/Stop 用量元数据合并）：`BalancePet-0.7.6-win-x64.zip`（SHA-256 `3C4E4FE1D2B64A060AC198AC177769AFF9395F73D6F1642A5E2261BDFC56656B`）；`BalancePet-0.7.6-Setup.exe`（SHA-256 `8D202BE0953C522571267BB22A4DB9269E054C673623749E51FDE2E696FAD91D`）。尚未提交或发布。
- `0.7.8` 发布包（Codex 停止钩子身份回填、Usage Event 空字段省略、扩展包路径和更新地址校验）：`BalancePet-0.7.8-win-x64.zip`（SHA-256 `ff850f8423d0ef6305ef1cb9bb0d43fd2b5b8e4b40cd7506fd74001398cb942e`）；`BalancePet-0.7.8-Setup.exe`（SHA-256 `8f4c307047a7b0e898a97a360691f7aac89bb8c0d21c0a3d7b184b78b40a863d`）。
- `Usage Analytics v0.2.2` 本地插件包：`balancepet.ext.feature.usage-analytics-0.2.2-win-x64.zip`（69,859,349 bytes，SHA-256 `317CE24491898B57F7B64C1FDF3B6CECC86954A03EFC0DA10097301D1F589530`）。尚未提交或发布；manifest 增加独立 GitHub Release 更新地址，插件仍可脱离主程序版本独立升级。
- `Usage Analytics v0.2.3` 本地插件包：`balancepet.ext.feature.usage-analytics-0.2.3-win-x64.zip`（72,048,353 bytes，SHA-256 `8AE7A3C9ED25CEC7A52F7E59D74156A378556E9E5BD5646DD84CB40406354018`）。尚未提交或发布；增加导航稳定性、完整用量事件测试说明，并把未提供的计数从 0 改为缺省字段；发布包不再携带被安全策略禁止的 `.ps1` 测试脚本。
- `Usage Analytics v0.2.4` 本地插件包：`balancepet.ext.feature.usage-analytics-0.2.4-win-x64.zip`（72,049,611 bytes，SHA-256 `F2B5C2C7824039991539B72C5E5EFC38FBF27D38ABD7FCD98BC1FE7C2F27896D`）。尚未提交或发布；增加无边框融合窗口、深色滚动条、实时刷新倒计时，并扩展客户端用量字段兼容。
- `0.3.4` 本地压缩包：`BalancePet-0.3.4-win-x64.zip`（200,236,187 bytes，SHA-256 `89752153EB5E47D2ECBF9853F45C3CC543492CFF9F6B56D763A2F0B7939A1D77`）。
- `0.3.4` 本地安装程序：`BalancePet-0.3.4-Setup.exe`（180,293,335 bytes，SHA-256 `5233D09665B524F38B1B5190AD7FF6FF29C50A96AD58F4336ECF542AF7607F3A`）。
- GitHub 发布、推送和版本号变更必须等用户明确授权；本轮用户已明确授权上传主程序与功能插件。

## 已完成能力

- 多账户配置、启用/停用、账户名称修改和配置导入/导出。
- 通用余额接口：自定义 URL、认证方式、Header 名、令牌、JSON 路径、货币、刷新间隔和低余额阈值。
- Bearer Token、完整 Authorization、websee-session、x-api-key、自定义 Header 等认证模式。
- DPAPI 保护本地令牌；凭据和余额请求留在应用侧，不交给无关服务。
- 后台自动刷新、手动刷新、余额缓存、低余额提示和桌宠状态切换。
- 长时间运行后的刷新状态清理、请求取消/超时处理、HTTP 连接轮换，以及保存多个账户名称后一次保存。
- Codex 任务状态桥接：工作中、成功、失败、暂停/终止等状态可以驱动桌宠表现。
- AI 登录状态桥接：通过 `BalancePet.Account.v1` 接收官方账户、官方 API、第三方 API 的元数据；令牌指纹匹配本地账户后自动切换，未匹配时仅提示配置。
- 预设形象目录：所有已登记模型都会出现在右键菜单、托盘菜单和设置窗口；只有完整 9 状态素材才会自动解锁，其余显示为灰色不可选。
- 当前已制作 5 个模型的完整 9 状态桌宠素材（见下表）。
- `v0.5.0` 扩展基础：资源型宠物 ZIP 清单、路径/大小/文件类型校验、独立安装目录、启用/禁用/卸载和设置界面管理；扩展不会加载 DLL 或执行脚本。
- 设置窗口按“账户与接口”“桌宠与交互”“扩展”“高级与迁移”分栏，底部保存操作固定显示；控件名称和既有保存逻辑保持兼容。
- 扩展规范位于 `docs/extension-spec/v1/`，打包工具为 `tools/package-pet-extension.ps1`。扩展与主程序通过稳定的九状态 PNG 合同解耦；在线目录和代码扩展留待后续版本。
- 功能扩展规范位于 `docs/extension-spec/feature-v1/`；首个独立插件位于 `extensions/BalancePet-Ext-Feature-UsageAnalytics/`。它使用 `balancepet.ext.feature.usage-analytics` 命名空间，读取 `usage-events.ndjson`，只统计 Token、缓存、请求成功率和耗时等元数据，不接触令牌、提示词或回复。`v0.6.0` 已提供隔离进程宿主、能力校验和启动/停止/卸载生命周期；任务桥接现在会记录完成请求的次数和耗时，客户端提供脱敏计数时还会记录 Token/缓存等字段。

## 当前待办

1. 使用本地 `v0.5.0` 测试包对资源扩展安装、启用、禁用和卸载进行运行时手工验收，确认当前形象回退和菜单刷新都正常。
2. 用真实客户端/CLI 接入 `tools/balancepet-usage.ps1` 或带计数的 `BalancePet.Task.v1` 事件，验证 Usage Analytics 插件显示真实 Token 事件。
3. 逐个把素材目录中的 13 个待制作角色制作成资源扩展；每次只处理一个模型，并根据角色特征调整动作提示词。
4. 完成下一轮修复或素材后，重新运行检查、打包，并在本文件追加版本记录。
5. 只有在用户确认后，才创建 Git 提交、推送和 GitHub Release。

## 版本记录

| 版本 | 状态 | 主要内容 |
| --- | --- | --- |
| `v0.1.0` | 已发布 | 首个可用版本。 |
| `v0.2.0` | 已发布 | 多显示器配置、账户/桌宠基础能力和首批桌宠素材。 |
| `v0.3.0` | 已发布 | 余额配置和桌宠交互能力持续完善。 |
| `v0.3.2` | 已发布 | 中英文安装程序和应用界面。 |
| `v0.3.3` | 已发布 | 长时间刷新、请求取消、连接轮换、多账户保存、README 统计卡片等修复和文档更新。 |
| `v0.3.4` | 本地已打包，未发布 | 修复边缘吸附距离和贴边空位问题，保留形象预设禁用/自动解锁、Grok 素材和文字自适应改动；等待用户确认后再提交和发布。 |
| `v0.4.0` | 已发布 | 增加 AI 登录状态本地桥接、官方/官方 API/第三方 API 分类、令牌指纹匹配本地账户和自动切换；已发布安装器与便携 ZIP。 |
| `v0.5.0` | 已发布 | 建立资源型宠物扩展规范、独立安装目录、ZIP 校验、启用/禁用/卸载和设置界面管理；同时修复设置界面本地化和菜单崩溃问题。 |
| `Usage Analytics v0.2.0` | 本地插件原型 | 深色控制台式只读用量仪表盘；概览卡片、提供方/模型拆分、7 天 Token 趋势、最近使用列表、Usage Event v1 读取、文件变更即时刷新和 60 秒兜底刷新；费用、余额、RPM 等协议未提供的字段明确显示为暂无数据。 |
| `Usage Analytics v0.2.1` | 本地已打包，未发布 | 侧边栏“仪表盘/使用记录/提供方与模型”可点击定位；主程序任务 Stop 支持缺少或轮换 turn_id 时匹配并记录耗时；兼容客户端上报的多种 Token/缓存/耗时字段别名；仅有生命周期事件时在状态栏说明 Token、模型和耗时需要客户端上报。 |
| `v0.6.0` | 本地已打包，未发布 | 功能扩展独立进程宿主、`usage.read` 能力声明、脱敏 Usage Event v1 命名管道、扩展库扫描/导入/选择安装、启用/禁用/启动/卸载；便携 ZIP 与安装器已生成，等待用户验收。 |
| `v0.7.0` | 本地已打包，未发布 | 同一扩展 ID 的多版本合并显示；行内图标化安装/卸载、启用/禁用、更新和启动；主程序独立的扩展更新频率设置、GitHub Release 元数据检查、缓存和下载校验；插件版本仍独立管理。 |
| `v0.7.1` | 本地已打包，未发布 | 修复更新频率选择始终回到每日的问题；扩大 Hook/Usage.v1 对嵌套用量字段的兼容范围；改善插件侧边导航刷新后的定位和生命周期-only 提示。 |
| `v0.7.6` | 本地已打包，未发布 | 扩大 Codex Hook、通用 Task.v1 和 Usage.v1 的用量字段兼容范围；统计插件采用无边框融合标题栏、深色滚动条并显示实时自动刷新倒计时。 |
| `v0.7.8` | 本地已打包，未发布 | 停止钩子缺少 stdin 身份时回填最近开始事件的会话/回合 ID，确保 Codex 本地 Token 记录可以写入用量事件。 |
| `Usage Analytics v0.2.2` | 本地已打包，未发布 | 增加 `update_url`，可由主程序按独立插件版本检查并安装更新；不改变用量事件协议。 |
| `Usage Analytics v0.2.3` | 本地已打包，未发布 | 增加完整用量事件测试说明；未上报的计数不再写入为 0；刷新或调整窗口后侧边导航定位更可靠。 |
| `Usage Analytics v0.2.4` | 本地已打包，未发布 | 统计窗口融合标题栏与滚动条样式，显示实时刷新倒计时，并扩展用量字段别名兼容。 |
| `Usage Analytics v0.2.9` | 准备发布 | 缓存命中率统一为 `Cache Read / Input`；趋势图增加 Input、Output、Cache Creation、Cache Read 和 Cache Hit Rate；用量窗口与任务栏使用 BalancePet ICO；左侧导航顺序与页面一致并随滚动自动高亮。ZIP SHA-256：`ee8de6360ecdefcbb11af2b5be8ac1f19441db1451e4ae79ca68d8fbe9d134a9`。 |

### 版本号规则

- 只修复 bug 或文档：递增补丁号，例如 `0.3.3` -> `0.3.4`。
- 增加向后兼容的新功能：递增次版本号，例如 `0.3.x` -> `0.4.0`。
- 破坏现有配置或使用方式的改动：递增主版本号。
- `0.x` 表示仍在快速迭代；达到功能和配置稳定、准备承诺兼容性时再发布 `1.0.0`。
- 每个 GitHub Release 都要记录构建文件名、SHA-256 和用户可读的变更摘要。

## 桌宠命名规范

### 显示名称

统一格式：

`<模型官方名> <形象名>「<两字国风名>」`

示例：`DeepSeek 小鲸鱼「澜汐」`、`ChatGPT 小白龙「霁珑」`、`Grok 小恶魔「烬斧」`。

- 模型名使用官方常见写法（例如 `MiniMax`、`MiMo`、`OpenCode`、`GPT Image 2`），不要混用文件名大小写。
- 形象名为 2 至 4 字的易读昵称，体现角色特征但不直接照抄模型名。
- 国风名固定使用两个汉字并放在中文直角引号 `「」` 中；同一项目内不重复。
- 英文界面使用 `<Model> <English appearance> "<Pinyin>"`，国风名保留拼音，不把中文字符硬塞进英文 UI。

### 文件和目录

- 目录键使用稳定的小写 ASCII：`deepseek`、`chatgpt`、`minimax`、`gemini`、`grok` 等。
- 每个目录必须包含 9 个状态文件，命名为：`idle.png`、`loading.png`、`success.png`、`error.png`、`low.png`、`inactive.png`、`clicked.png`、`codex-working.png`、`codex-done.png`。不能自行增加或改名，缺少状态时由程序按既定规则回退。
- 新角色先在本文件登记名称和目录键，再开始生成素材；名称未确认前不要写入 UI 菜单。

## 形象素材制作流程

1. 以 `素材/二次元形象/<模型文件>` 中的 idle 参考图为唯一角色依据，先确认发色、服装、配饰、武器/道具和主色调。
2. 使用 `素材/提示词.txt` 的通用约束作为固定前缀：Q 版二次元、单角色、透明背景、清晰轮廓、完整可见、适合作为桌面宠物；明确禁止文字、水印、UI、边缘裁切和多余人物。
3. 不生成全身立绘作为最终桌宠图。按现有素材的半身/近景构图生成，保留头部、上身和关键配饰，给气泡和状态灯留出安全边距。
4. 一次只制作一个模型。通用动作（idle、loading、success、error、low、inactive、clicked、Codex 工作/完成）可以复用，但姿势、表情、道具和光效要贴合该角色，不能把其他模型的提示词原样套用。
5. 输出统一为带透明通道的 PNG，尺寸和裁切方式与现有 `versions/csharp-wpf/assets/pets/` 素材一致；检查边缘是否被裁掉、头发/武器是否贴边、透明区域是否干净。
6. 将通过检查的文件放到对应的小写目录，逐项核对状态映射、资源嵌入和运行时显示；未完成的状态不要伪装成已完成。
7. 完成一个模型后更新本文件的资产状态；完成一个版本后再更新版本记录、运行 `npm run check`（Electron 改动）或对应的 .NET/Python 验证命令，并记录结果。

## 资产状态与预设名称

素材来源目录：`C:\Users\GoldenMoon\Desktop\素材\二次元形象`。下表中的“待制作”仅表示已经为角色预留名称，不能当作程序已经提供该桌宠。

| 模型 | 参考图文件 | 预设显示名称 | 目录键 | 状态 |
| --- | --- | --- | --- | --- |
| DeepSeek | `DeepSeek.avif` | `DeepSeek 小鲸鱼「澜汐」` | `deepseek` | 已完成 |
| ChatGPT | `ChatGPT.avif` | `ChatGPT 小白龙「霁珑」` | `chatgpt` | 已完成 |
| MiniMax | `MiniMax.webp` | `MiniMax 小海螺「绯音」` | `minimax` | 已完成 |
| Gemini | `Gemini.webp` | `Gemini 小星猫「星璃」` | `gemini` | 已完成 |
| Grok | `Grok.webp` | `Grok 小恶魔「烬斧」` | `grok` | 已完成 |
| Claude | `Claude.webp` | `Claude 小书灵「丹笺」` | `claude` | 待制作 |
| Kimi | `Kimi.webp` | `Kimi 小棱镜「虹谱」` | `kimi` | 待制作 |
| Qwen | `Qwen.webp` | `Qwen 小折扇「绀华」` | `qwen` | 待制作 |
| Ernie | `Ernie.jpg` | `Ernie 小病书灵「青绡」` | `ernie` | 待制作 |
| GLM | `GLM.avif` | `GLM 小方灵「青棱」` | `glm` | 待制作 |
| GPT Image 2 | `GPT Image2.jpg` | `GPT Image 2 小墨龙「玄珏」` | `gpt-image2` | 待制作 |
| Llama | `Llama.jpg` | `Llama 小羊驼「绒眠」` | `llama` | 待制作 |
| MiMo | `Mimo.jpg` | `MiMo 小兔码师「橙析」` | `mimo` | 待制作 |
| Mistral | `Mistral.jpg` | `Mistral 小猫骑士「麦霜」` | `mistral` | 待制作 |
| OpenCode | `Opencode.avif` | `OpenCode 小码灵「墨枢」` | `opencode` | 待制作 |
| Perplexity | `Perplexity.jpg` | `Perplexity 小探灯「青鉴」` | `perplexity` | 待制作 |
| RWKV | `RWKV.jpg` | `RWKV 小乌鸦「夜翎」` | `rwkv` | 待制作 |
| Seedence | `Seedence.jpg` | `Seedence 小星晶「澄芽」` | `seedence` | 待制作 |

如果后续发现某张参考图与模型对应关系有误，先改本表，再改目录键或 UI，避免名称和素材在不同地方分叉。

## 安全与验证底线

- 不在日志、提交、测试夹具、截图或发布说明中写入真实 API 令牌。
- 令牌必须继续使用 Windows DPAPI 保护；渲染层/界面层不应接触明文令牌。
- 优先使用中转站公开记录的余额接口，不自动化登录页面；如要做站点专用适配器，必须先确认接口和会话风险。
- 最小刷新间隔保持 30 秒；请求超时、取消、切换账户和关闭设置窗口时都要结束旧任务并清理状态。
- Electron 改动运行 `npm run check`；Python 改动运行：

  ```powershell
  python -m py_compile .\versions\python\balance_pet.py .\versions\python\balance_pet_qt.py .\versions\python\balance_provider.py
  python -c "import sys; sys.path.insert(0, r'versions\\python'); import balance_pet as b; assert b.read_path({'data': {'balance': 1.25}}, 'data.balance') == 1.25"
  ```

- C# WPF 改动至少运行：

  ```powershell
  dotnet build .\versions\csharp-wpf\BalancePet.Wpf.csproj -c Release --no-restore
  ```
