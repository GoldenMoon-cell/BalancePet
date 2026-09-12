# BalancePet Usage Analytics

`balancepet.ext.feature.usage-analytics` is an independent, read-only usage
dashboard for BalancePet. It presents a dark console-style dashboard with
overview cards, provider/model breakdowns, a seven-day token trend, and recent
requests. It summarizes locally recorded request metadata: token counts, cache
reads, request success, time to first token, throughput, provider, account, and
model.

The cache metric follows the relay-console convention: `Cache Hit Rate` is
`Cache Read / Input`, where `Input` is the complete input total and the chart's
uncached Input series is `Input - Cache Read`. The seven-day chart also shows
Output and Cache Creation so the four token series can be compared directly.

The sidebar navigation is active: “仪表盘” returns to the overview, “使用记录”
jumps to recent requests, and “提供方与模型” jumps to the provider/model
breakdown. When the host has recorded only task lifecycle events, the status
bar says so explicitly instead of implying that a zero Token value is a parser
failure.

The extension never receives or reads API tokens, prompts, replies, cookies, or
provider websites. It reads only `%LOCALAPPDATA%\\BalancePet\\usage-events.ndjson`
and accepts an optional `--data-dir` argument for testing or portable setups.
The dashboard refreshes automatically at least once per minute, shows a live
countdown in the header, and also reacts
to changes in the event files, so a client that reports after each request is
normally visible within a few seconds. Refresh remains available for manual
verification. BalancePet task lifecycle events can also contribute a request
and duration record; token and cache cards become non-zero only when the client
provides those counters in the Usage.v1 payload.

Codex's lifecycle Hook does not expose provider usage counters on every client
version. In that case the request count and locally measured duration are still
valid, while Token/model/cache fields intentionally show “未上报”. To verify a
full event locally, run the sender shipped with the BalancePet core package
while BalancePet is running:

```powershell
.\tools\balancepet-usage.ps1 -Provider Codex -Model gpt-5.6-sol `
  -InputTokens 64100 -OutputTokens 904 -CacheReadTokens 9000 `
  -DurationMs 98000 -TimeToFirstTokenMs 18500 -ToolCalls 0 -Steps 5
```

The independent extension ZIP intentionally does not include executable
scripts; the sender is available in the main program's `tools` directory. It
transmits counters only over the local named pipe. It does not read or accept
prompts, replies, cookies, API keys, or other credentials.

This package implements the shipped Feature Extension API v1 and requires
BalancePet `0.7.7` or newer for Codex Token and cache counters. It still shows
request metadata on older cores, but those cores cannot populate the counters.
BalancePet `0.5.x` supports resource-only pet extensions and will not install
this executable extension yet.

The feature-extension contract does not require this dashboard's dark theme,
logo, window layout, or navigation. Those are implementation choices of this
official extension. Third-party feature extensions can provide a completely
different UI as long as they follow the manifest, out-of-process launch,
capability, and local-data rules in
[`docs/extension-spec/feature-v1/README.md`](../../docs/extension-spec/feature-v1/README.md).

## Build

From the repository root:

```powershell
.\tools\package.ps1
```

The resulting ZIP is independent from the main program and can be released in
this extension's own GitHub repository.

For local protocol testing, the repository source includes
`tools/emit-usage-event.ps1`, while the distributable core package provides the
same protocol through `tools/balancepet-usage.ps1`. The sender writes only
counters and timings; it never accepts or writes prompts, replies, cookies, or
API tokens.

Contract: [BalancePet Feature Extension Specification v1](../../../docs/extension-spec/feature-v1/README.md).
