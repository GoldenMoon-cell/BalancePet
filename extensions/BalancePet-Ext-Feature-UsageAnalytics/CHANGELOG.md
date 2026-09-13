# Changelog

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
