# Changelog

## 0.4.0

### Features

- Declare an account subscription-billed. A subscription has no per-request price at all, so "no cost reported" is the expected state rather than a gap in the data. The account settings gain a switch that says so, and the account directory carries the declaration through to the dashboard.
- Read the plan the client reports. The host now forwards the subscription plan it observed for a session. The two signals are independent and both count: the declaration covers subscriptions whose client never reports a plan, and a reported plan covers accounts the user never labelled.
- Say what the money cards mean on a subscription account. Balance, today's usage and total usage each read 订阅制 · 不计费 or 订阅制 · 无余额 with a line explaining why, instead of showing a zero that looks like a failed reading.

### Fixes

- Tell an absent cost apart for official accounts and relays. A relay that reports no cost is missing data; an official API account under a subscription genuinely has none. The row text no longer reads the same for both.
- Stop clipping the official-account cost text. It was drawn on one line in a fixed-height box, so the longer explanation was cut off mid-sentence.
- Default `EventRow.AccountLabel` to empty rather than null. The constructor returns early when the host reported no account, and `ProviderLine` and `HasAccount` both read the value without a null check, so a record with no account id could throw while being rendered.

### Notes

- Requires BalancePet core `1.4.0` or newer. The reported plan arrives on a field the core only publishes from that version, and on an older core the subscription features stay hidden rather than showing figures that are wrong.

## 0.3.22

### Features

- Show which account each record billed to, next to the client: `DeepSeek Harness · 何意味`. Names come from the host settings; an unknown id falls back to a shortened form.
- Split the detail view by where the cost came from. A relay-matched cost keeps the per-request layout; a balance-derived one labels itself 任务总消耗, explains that it is the account balance change across the task rather than a per-request charge, and hides the three relay panel — cost timeline, token trend and relay request list — instead of showing empty placeholders.

### Notes

- An official API account publishes no per-request billing log, so the host reports a task-level figure from the balance drop. It is a real charge, including any peak/off-peak discount, but it has polling granularity: tasks shorter than the account refresh interval can measure zero.

## 0.3.21

### Fixes

- Line up the navigation rows. The base button template hard-coded a centred content presenter and ignored `HorizontalContentAlignment`, so `NavButton`'s `Left` never applied and each row was centred; the shorter label pushed its icon out of line. The template now honours the property, and the default Centre is stated explicitly so ordinary buttons are unaffected.
- Make the three navigation glyphs one family: the folder was stroked while the other two were filled, and its bounding box differed, so `Stretch="Uniform"` normalised them differently. All three now share a filled `(2,2)-(12,12)` box.
- Correct the toolbar refresh glyph. Its geometry is an open arc, but it was being filled, which rendered as a blob rather than an arrow; it is now stroked with round caps.

### Improvements

- Rebuild the toolbar Refresh button on the shared palette at a 36 px pointer target, matching the account selector beside it, instead of the cramped ad-hoc accent pill.
- Add motion: navigation icons scale slightly on hover, and the refresh icon completes one turn per click so the press is visibly acknowledged.

## 0.3.20

### Improvements

- Give the history list a bounded height so it virtualizes and recycles its rows. Switching the client filter took seconds with a few hundred records and now measures around 0.17 s regardless of how many are shown.
- Make a client filter change re-render only the list instead of re-reading every file, rebuilding the report and redrawing the dashboard charts.

### Changes

- Remove the 60-second auto-refresh and its countdown badge. The host writes a file for every new record, so the file watchers already keep the view current and the timer was duplicate work. A Refresh button in the header replaces it, re-reading the data directory on demand.
- Signal the host to refresh once when the window opens, so opening it shows current balances without polling.

## 0.3.19

### Features

- Add client filter chips to the usage history, one per reporting provider with an event count, so Codex, DeepSeek Harness and any other client can be viewed separately instead of interleaved.
- Add an account selector to the dashboard header. Every balance, usage and today figure now follows the selected account, and the combined view sums them, so the three numbers always describe the same scope.
- Resolve opaque account ids to the names chosen in the host settings, reading only `monitors[].id` and `monitors[].name`.

### Notes

- The dashboard previously mixed scopes: the balance snapshot was summed across accounts while the usage total came from the host's selected account alone. The combined entry is now the default and applies to both.

## 0.3.18

### Improvements

- Keep the Token trend hover surface active across the full plot area, including translucent filled regions.
- Position the tooltip near the cursor so it remains visible while moving through the chart.

## 0.3.17

### Improvements

- Make the Token trend tooltip follow the mouse position across the chart instead of snapping the guide to a data point.
- Add translucent area fills beneath the Input, Output, Cache Creation, and Cache Read series.

## 0.3.16

### Improvements

- Add hover details to the dashboard Token trend chart, including date, token counters, cache hit rate, and request count.
- Add a vertical guide line so the hovered time bucket is easy to follow.

## 0.3.15

### Improvements

- Move the dashboard time-range selector into a dedicated analysis filter bar.

## 0.3.14

### Improvements

- Reorganize the dashboard into a two-row, four-column metric layout inspired by relay dashboards.
- Move balance, cumulative spending, and server daily usage into a dedicated account-and-site summary panel.
- Keep provider/model breakdowns and token trends in the lower analytical area for a clearer hierarchy.

## 0.3.13

### New features

- Clarify the dashboard's local balance-change estimate versus the server's daily usage summary.
- Add cumulative balance-ledger spending to the total-balance card.

## 0.3.12

### New features

- Add a per-request Token trend chart to usage-record details for input, cache-read, and output counters.
- Leave gaps for counters that were not reported instead of rendering missing data as zero.

## 0.3.11

### New features

- Replace inline expandable request rows with a full usage-record detail view opened by clicking the entire record card.
- Add Token composition bars, a per-request cost timeline, and a linked request detail table with back/Esc navigation.

## 0.3.10

- Continue consuming the host-written per-request cost and reasoning details without reading provider credentials.
- Refresh usage rows when the main program completes browser-session based cost reconciliation.

## 0.3.9

- Increase the usage-event line limit so tasks with many per-request details remain readable.
- Keep the task summary and expandable request details compatible with the new main-program usage pipeline.

## 0.3.8

- Display the reasoning effort captured by the host for Codex tasks and matched relay requests.

## 0.3.7

- Avoid reparsing unchanged usage-event files on every refresh; only changed or newly rotated files are rescanned.
- Keep the existing report totals and event de-duplication behavior while reducing repeated disk I/O.

## 0.3.6

- Add selectable Token trend ranges: 24 hours, 7 days, 30 days, 90 days, and all time.
- Switch the chart bucket size to hour, day, week, or month for readable labels at each range.

## 0.3.5

- Show relay-reported reasoning intensity under each task model.
- Expand a task row to inspect its matched relay request details while keeping one summary row per task.
- Display the summed relay cost for tasks whose individual requests can be reconciled safely.

## 0.3.4

- Add the server-reported daily `/v1/usage` cost summary to the dashboard without attributing aggregate charges to individual requests.
- Keep the extension credential-free; the main application publishes only a sanitized local summary snapshot.

## 0.3.3

- 左侧导航移除“提供方与模型”入口；提供方与模型统计仍保留在仪表盘右侧内容中。
- 修复仪表盘滚动后错误高亮“使用记录”的问题；高亮现在跟随当前打开的页面。

## 0.3.2

- 自动同步间隔调整为 60 秒，事件文件变化仍会立即刷新界面。
- 使用记录支持同一事件的延迟补齐，Codex 在任务结束后才写入的 Token 数据会更新原记录，不再产生重复请求。
- 按日志文件写入时间合并轮转文件，避免补齐记录被旧文件覆盖。

## 0.3.1

- 移除无实际接口刷新能力的手动刷新按钮，改为文件监听、5 秒定时同步和主程序刷新调度信号三重更新机制。
- “使用记录”改为独立页面，并逐条标注输入给模型、模型输出、缓存读取、缓存写入和消耗额度。
- Usage Event v1 增加可选 `cost` 与 `currency` 字段；未上报额度时明确显示“未上报”，不根据 Token 猜测金额。
- 窗口圆角和控件圆角按 Windows 11 / Windows 10 自动调整。

## 0.3.0

- Add a total balance card sourced from the host's credential-free Balance Usage v1 snapshot.
- Sum configured account balances only when currencies match and keep the existing estimated-spending semantics.

## 0.2.12

- Replace the unavailable TTFT overview card with today's spending from the
  host's credential-free `balance-usage.v1.json` summary.
- Follow the selected BalancePet account and refresh when the host summary changes.
- Document the balance-usage summary and its estimate-only semantics.

## 0.2.11

- Rename the first-token card to average time to first token (TTFT).
- Show `暂无数据 / No data` when no client has reported TTFT instead of implying a zero or parser failure.
- Keep the average restricted to valid, explicitly reported `time_to_first_token_ms` samples.

## 0.2.10

- Rewrite the README as a Chinese/English bilingual usage, privacy, compatibility, and build guide.
- Document the release-note structure and link directly to the Feature Extension API v1 contract.

## 0.2.9

- Align cache-hit calculations with relay dashboards: cache read is divided by the complete input total.
- Add a multi-series seven-day trend for uncached input, output, cache creation, cache read, and cache-hit rate.
- Use the BalancePet application icon for the usage window and taskbar instead of the generic Windows placeholder.

## 0.2.8

- Read Token and cache counters emitted by BalancePet 0.7.7 for Codex tasks.
- Keep unavailable counters explicit when the client does not expose usage data.

## 0.2.7

- Display missing token counters as unreported instead of zero.
- Keep the dashboard and recent-list scrollbars integrated with the dark theme.
- Format long task durations as minutes or hours for easier scanning.

## 0.2.6

- Unified dashboard and recent-list scrollbars with the dark theme and removed the bright system focus outline.

## 0.2.5

- Fixed the dashboard startup crash caused by a read-only model percentage binding being treated as two-way.

## 0.2.4

- Added a borderless integrated title bar and styled scrollbars for a cohesive dashboard window.
- Added a live seconds-until-refresh countdown in the header.
- Broadened compatible usage counter aliases for client hook payloads.

## 0.2.3

- Improve navigation reliability after refresh or window resize.
- Explain lifecycle-only data and the local full-usage event sender.
- Treat omitted counters as “not reported” in the bundled test sender.

## 0.2.2

- Added the extension-owned GitHub Release update URL to the manifest.
- Kept the executable and Usage Event v1 contract unchanged; this package can
  be installed independently from BalancePet core updates.

## 0.2.1

- Sidebar entries now navigate to the dashboard, recent usage, and provider/model sections.
- Added more lifecycle payload aliases and session fallbacks so compatible clients
  can populate model, duration, and token fields instead of producing count-only events.
- Clarified that lifecycle-only events cannot provide token totals without client counters.

## 0.2.0

- Replaced the compact report window with a dark dashboard layout inspired by
  provider consoles.
- Added overview cards, provider breakdown, model distribution, seven-day token
  trend, and a richer recent-usage list.
- Metrics remain metadata-only; unavailable token or cost fields are shown as
  unavailable instead of being inferred.

## 0.1.3

- Clarified the empty-data status so missing host events are easier to diagnose.

## 0.1.2

- Enlarged the dashboard window and made it resizable.
- Refined the header, summary cards, recent-request panel, buttons, and refresh status.

## 0.1.1

- Refresh automatically when usage event files change.
- Refresh at least once per minute as a fallback when file notifications are unavailable.

## 0.1.0

- Initial read-only dashboard for BalancePet Usage Event v1.
- Shows 5-hour, 24-hour, 7-day, and all-time token totals.
- Shows cache hit rate, request count, success rate, average first-token time,
  output throughput, and recent request metadata.
# 0.3.9

- 提高明细事件行的读取上限，支持主程序保存更多逐次 API 请求记录。
