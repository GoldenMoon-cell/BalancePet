# Changelog

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
