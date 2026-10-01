# BalancePet 用量统计 / BalancePet Usage Analytics

`balancepet.ext.feature.usage-analytics` 是 BalancePet 的独立只读用量统计插件。
它提供深色仪表盘、概览卡片、提供方与模型拆分、近 7 天 Token 趋势和最近请求列表，
汇总本地记录的请求元数据：Token 数量、缓存读取、请求成功率、输出速度、
提供方、账户和模型。

`balancepet.ext.feature.usage-analytics` is an independent, read-only usage dashboard for
BalancePet. It provides overview cards, provider/model breakdowns, a seven-day token trend,
and recent requests based on locally recorded metadata: token counts, cache reads, request
success, time to first token, throughput, provider, account, and model.

## 数据口径 / Metric definitions

缓存指标遵循中转站统计口径：`Cache Hit Rate = Cache Read / Input`。
其中 `Input` 是完整输入总量，趋势图中的未缓存输入为 `Input - Cache Read`。
趋势图同时展示 `Output`、`Cache Creation` 和 `Cache Read`，便于对照四类 Token 数据。

The cache metric follows the relay-console convention: `Cache Hit Rate = Cache Read / Input`.
`Input` is the complete input total, while the chart's uncached input series is
`Input - Cache Read`. The chart also shows `Output`, `Cache Creation`, and `Cache Read`.

## 页面与导航 / Dashboard and navigation

左侧导航包含两个视图：

- **仪表盘**：概览卡片、提供方拆分、模型分布和 Token 趋势。顶栏的账户下拉可在「全部账户（合计）」与单个账户之间切换，「本地余额快照」「累计余额变化」「余额变化估算」会一起跟随，三个数字始终描述同一个范围。账户名取自主程序设置里账户的显示名，取不到时退化为缩短的账户 ID。
- **使用记录**：最近请求、耗时、模型以及已上报的 Token 数据。顶部是按客户端生成的筛选标签（`全部` / `Codex` / `DeepSeek Harness` / …），每个标签带该客户端的记录条数；只影响这份列表，仪表盘的汇总口径不变。某个客户端不再有记录时，筛选会自动退回「全部」，列表不会卡在空状态。

The sidebar contains two views:

- **仪表盘 / Dashboard**: overview cards, provider split, model distribution, and token trend. The account selector in the header switches between the combined view and a single account, and the balance snapshot, cumulative change and balance-based estimate all follow it, so the three figures always describe one scope. Names come from the host settings; an unknown account falls back to a shortened id.
- **使用记录 / Recent usage**: recent requests, duration, model, and reported token data. Client chips above the list (`全部` / `Codex` / `DeepSeek Harness` / …) each carry that client's record count and filter only this list — the dashboard aggregates stay unchanged. A filter whose client no longer reports falls back to "all" so the list can never be stuck empty.

如果主程序只记录了任务生命周期事件，状态栏会明确显示这一点，不会把 Token 为 0
误解为解析失败。If the host has only recorded task-lifecycle events, the status bar says so
explicitly instead of treating a zero Token value as a parser failure.

## 数据来源与隐私 / Data source and privacy

插件不会接收或读取 API Token、提示词、回复、Cookie，也不会打开提供方网站。
默认只读取：

```text
%LOCALAPPDATA%\\BalancePet\\usage-events.ndjson
%LOCALAPPDATA%\\BalancePet\\csharp-settings.json   （仅 monitors[].id 与 monitors[].name）
```

第二个文件用于把账户 ID 显示成账户名。它同时保存着受 DPAPI 保护的访问令牌，因此插件
以 DOM 方式解析，只取出 `monitors[].id` 和 `monitors[].name` 两个字符串字段，不把文档
反序列化成可能携带凭据的模型，也不会记录或转发文件内容。

The second file maps account ids to display names. It also holds DPAPI-protected access
tokens, so it is parsed through a DOM and only the `monitors[].id` and `monitors[].name`
strings are ever materialized; the document is never deserialized into a model that could
carry credentials, and its contents are never logged or forwarded.

也可以通过 `--data-dir` 指定测试目录或便携版数据目录。插件只读取脱敏的计数和耗时，
通过本地命名管道接收客户端主动上报的 Usage Event v1 数据。

The extension never receives or reads API tokens, prompts, replies, cookies, or provider
websites. It reads only the local Usage Event v1 files and accepts an optional `--data-dir`
argument for testing or portable setups. Only sanitized counters and timings are used.

仪表盘在使用记录、余额快照或站点用量文件变化时自动刷新，此外顶栏的「刷新」按钮可以手动重读数据目录。**没有定时轮询**：
主程序为每条新记录都会写文件，轮询只是重复劳动。打开窗口时会向主程序发一次无凭据的刷新信号（仅此一次，不是轮询），
主程序收到后仍遵守自身的最短刷新间隔。只有客户端主动上报 Token、缓存和首 Token 延迟（TTFT）字段时，
对应卡片才会显示数值；没有主程序余额记录时“余额变化估算”卡片会显示“暂无数据”。
插件还会读取主程序提供的当前账户余额快照，在“总余额”卡片中显示所有已配置账户的合计（仅在币种一致时求和）。

“余额变化估算”卡片读取主程序发布的脱敏
`%LOCALAPPDATA%\\BalancePet\\balance-usage.v1.json`。它与主程序“用量统计”
窗口使用同一份本地余额变化账本：仅统计两次成功余额查询之间的余额下降，
不代表中转站账单或每次请求的精确价格。没有成功余额记录时显示“暂无数据”。
“总余额”区块中的“总消费”读取同一余额账本的累计值；币种切换后会重新开始累计，
因此它表示当前币种下主程序已记录的累计余额下降，而不是中转站账单总额。

对于 New API 兼容的中转站，主程序会在同一站点提供只读日志接口时，将服务器返回的
`quota` 按站点公开的额度换算规则回写到对应请求；其他中转站、接口拒绝日志查询或无法可靠匹配的记录仍显示“未上报”，不会按公开模型价格猜测。

对于只提供 OpenAI 兼容 `/v1/usage` 的站点，仪表盘会额外显示主程序读取的“站点汇总额度”。
该值来自接口的 `daily_usage.actual_cost`（没有时回退到 `cost`），按日期汇总，不能代表某一条请求的精确费用；
历史记录中的单条额度仍会显示“未上报”。

The dashboard refreshes when the usage, balance or server-usage files change, and the header's Refresh button
re-reads the data directory on demand. There is no polling timer: the host writes a file for every new record,
so polling only repeated work. Opening the window signals the host once (not a poll); the host still honours its
own minimum refresh interval. Token and cache cards
are populated only when the client reports those counters; the “Balance-change estimate” card
is populated from the host's balance-usage summary.
The “Balance-change estimate” card reads the credential-free
`%LOCALAPPDATA%\\BalancePet\\balance-usage.v1.json` summary published by the host.
It uses the same local balance-change ledger as the core usage window: only decreases
between successful balance observations count, so it is not a relay invoice or an exact
per-request price. It shows “暂无数据 / No data” until a successful balance observation exists.
The total balance card reads the host's credential-free balance snapshot and sums accounts only
when their currencies match; no provider token is included in the snapshot. Its “Total spending”
value is the cumulative balance decrease tracked by the host for the current currency.

For relays that expose only the OpenAI-compatible `/v1/usage` endpoint, the dashboard also shows
a credential-free “server usage summary” read by the host from `daily_usage.actual_cost` (falling
back to `cost`). It is a daily aggregate, not an exact per-request charge; individual history rows
remain “unreported” when no request log is available.

Codex 的生命周期 Hook 并不保证所有客户端版本都提供提供方用量计数。
这种情况下，请求次数和本地测得的耗时仍然有效，而 Token、模型和缓存字段会显示为未上报。

Codex lifecycle hooks do not expose provider usage counters on every client version. Request
counts and locally measured duration remain valid, while token/model/cache fields stay unreported.

## 本地测试 / Local testing

主程序运行时，可以使用随 BalancePet 一起发布的发送脚本写入一条完整用量事件：

```powershell
.\\tools\\balancepet-usage.ps1 -Provider Codex -Model gpt-5.6-sol `
  -InputTokens 64100 -OutputTokens 904 -CacheReadTokens 9000 `
  -DurationMs 98000 -TimeToFirstTokenMs 18500 -ToolCalls 0 -Steps 5
```

When BalancePet is running, use the sender shipped with the core package to write a complete
local usage event. The sender transmits counters only over the local named pipe and never reads
or accepts prompts, replies, cookies, API keys, or other credentials.

独立插件 ZIP 刻意不包含可执行脚本；测试发送器位于主程序包的 `tools` 目录。
仓库源代码仍包含 `tools/emit-usage-event.ps1`，用于本地协议测试。

The independent extension ZIP intentionally excludes executable scripts. The repository source
contains `tools/emit-usage-event.ps1` for local protocol testing, while the distributable core
package provides `tools/balancepet-usage.ps1`.

## 兼容性与插件规范 / Compatibility and extension contract

本插件实现已发布的 **BalancePet Feature Extension API v1**，需要 BalancePet `0.7.7`
或更高版本才能读取 Codex Token 和缓存计数；旧核心仍可显示请求元数据，但不能填充这些计数。
插件版本独立于主程序版本递增，当前插件版本为 `0.3.4`。

This package implements the shipped **BalancePet Feature Extension API v1** and requires
BalancePet `0.7.7` or newer for Codex token and cache counters. Older cores can still show
request metadata but cannot populate those counters. The extension version is independent from
the core version; the current package version is `0.3.13`.

功能扩展规范只约束 manifest、独立进程启动、能力声明、Usage Event v1、本地数据目录、
更新和安全校验；不要求统一插件的 UI 工具包、主题、Logo、字体、语言或窗口布局。

The feature-extension contract covers the manifest, out-of-process launch, capabilities,
Usage Event v1, local data directory, update behavior, and package safety. It does not require
a shared UI toolkit, theme, logo, typography, language, or window layout.

规范全文：
[BalancePet Feature Extension Specification v1](https://github.com/GoldenMoon-cell/BalancePet/blob/main/docs/extension-spec/feature-v1/README.md)

Full contract:
[BalancePet Feature Extension Specification v1](https://github.com/GoldenMoon-cell/BalancePet/blob/main/docs/extension-spec/feature-v1/README.md)

## 构建 / Build

在仓库根目录执行：

```powershell
.\\tools\\package.ps1
```

Run from the repository root:

```powershell
.\\tools\\package.ps1
```

生成的 ZIP 可独立发布到本插件自己的 GitHub 仓库，不要求同步发布 BalancePet 核心版本。
The resulting ZIP can be released independently from BalancePet core updates.

## 许可证 / License

本插件使用 MIT 许可证。This extension is licensed under the MIT License; see [LICENSE](LICENSE).
