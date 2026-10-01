# BalancePet → DeepSeek Harness 桥接插件

把 DeepSeek Harness 的回合（turn）开始与结束上报给 BalancePet 桌宠，让桌宠在 DSH 干活时显示 `codex-working`、干完显示 `codex-done` 并刷新余额。

## 为什么是插件而不是 hook

Codex 那条链能做成「写一个 `hooks.json` 就完事」，是因为 Codex 自己提供了配置式的 shell 钩子。**DeepSeek Harness 没有这个机制**：它的包列表里没有任何 hook 包，`hook/invoked` 只是会话日志里的事件类型、没有任何发射方，官方可订阅的会话事件只有

```
session/created   session/disposed   session/event   session/flush
```

而这些只能由**进程内的 Cordis 插件**订阅。所以 DSH 的「hook」就等于一个 Cordis 插件。

## 契约

插件是一个零依赖的 ESM 包，默认导出 Cordis 接受的插件对象（`@deepseek-ai/cordis` 的 `Loader.resolve` 接受函数或带 `apply` 的对象）：

```js
export default {
  name: 'balancepet-dsh-bridge',
  apply(ctx) {
    ctx.on('session/event', (session, event) => { /* ... */ });
  },
};
```

它过滤出官方的 `turn/start` 与 `turn/end`（见 `@deepseek-ai/dsh-session` 的 `KNOWN_SESSION_EVENT_TYPES`），然后把下面这一行 JSON 写进命名管道 `\\.\pipe\BalancePet.Task.v1`：

```json
{"state":"start","sessionId":"dsh:<会话 id>","turnId":"turn:<回合序号>","provider":"DeepSeek Harness"}
```

`turn/end` 会复用 `turn/start` 报出的同一个 `turnId`，这样 BalancePet 能按精确 key 配对上，不会误结束别的任务。`provider` 必须与主程序里的 `CodexTaskBridge.DeepSeekHarnessProvider` 保持一致。

### 契约来源（已实测）

会话事件对象来自 `@deepseek-ai/dsh-session` 的类型声明：

```ts
type SessionEvent = { type: K; seq: number; time: number; data: SessionEventMap[K]; ignorable?: true }
class Session { get id(): SessionId }
```

用 `tools/dsh-bridge/inspect-dsh-turn-events.mjs` 解出真实会话日志（1128 条记录）后核对到的实际记录形状：

```json
{"type":"turn/start","seq":5, "time":1790777440878,"data":{"turn":…}}
{"type":"turn/end",  "seq":41,"time":1790778707815,"data":{"turn":…,"reason":…}}
```

所以插件用 `event.type` 判别、用 `session.id` 取会话、用 `event.seq` 兜底生成回合 id，都是对着真实数据核过的。`data.turn` 是语义上的回合号，需要更稳的编号时可以改用它。

用这个工具排查“DSH 到底有没有发回合事件”：

```powershell
node tools\dsh-bridge\inspect-dsh-turn-events.mjs "%USERPROFILE%\.dsh\sessions\<工作区>\<会话>\session.v4.jsonl.zstd"
```

## 设计约束

这个插件**跑在 DSH 宿主进程里**，而 Codex hook 是独立子进程 —— 所以它的健壮性直接关系到 DSH 的稳定。代码因此遵守：

- 所有回调包 `try/catch`，永不抛出；
- `apply()` 无论拿到什么 `ctx` 都不抛；
- 连接按事件建立、纯尽力而为：**BalancePet 没运行是正常情况，不是错误**；
- 不持有定时器和长生命周期句柄。

另外管道连接带 3 次重试：BalancePet 的管道实例在两次连接之间会重建，紧随其后的消息会瞬时 `ENOENT` 失败。

## 安装

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\install-balancepet-dsh-plugin.ps1"
```

做两件事：把本目录复制到 DSH profile 下，并在该 profile 的 `cordis.patch.yml` 追加一条挂载项：

```yaml
- insert:
    - id: balancepet-dsh-bridge
      name: ./balancepet-dsh-bridge/lib/index.js
```

用相对说明符是有意的：`@deepseek-ai/cordis-plugin-loader` 会把以 `.` 开头的说明符按 profile 目录解析，因此**不需要包管理器、不需要联网、不改动 profile 的 `package.json`**。

注意 `desktop` profile 由 Electron 应用独占管理（`dsh plugin --profile desktop ...` 会被 CLI 直接拒绝），所以这里全程不调用 CLI。

装完**通常不需要重启 DeepSeek Harness**：它会自动重新加载 profile（实测安装后 3 秒内插件就被加载，日志出现 `bridge active`）。如果日志里没有这一行，再从托盘退出并重新启动它。之后到 BalancePet 设置窗口的「**AI 联动**」页勾选「DeepSeek Harness」（该页每个客户端一个独立开关，Codex、Gemini、Qwen、Claude 各占一项，另有「其他客户端」兜底）。

也可以完全跳过手动安装：在「AI 联动」页勾选 DeepSeek Harness 并保存，BalancePet 会自动调用本脚本。

`-Action Status` 只查看，`-Action Uninstall` 回滚（会先备份 `cordis.patch.yml`）。

## 诊断

```text
%LOCALAPPDATA%\BalancePet\dsh-bridge.log
```

记录 `apply()` 是否被调用、订阅是否成功、每个回合事件、上报的用量、以及管道投递结果（`sent` / `error:ENOENT` / `timeout`）。文件超过 256 KB 会自动清空。设置 `DSH_BRIDGE_DEBUG=1` 可以看到每次连接尝试的细节；设置 `DSH_BRIDGE_DRY_RUN=1` 则只记录不投递，自检用的就是这个。

正常情况下应该依次看到 `bridge active` → `turn/start ...` → `turn/end ...`（→ `usage {...}`）。如果只有 `bridge active` 而没有任何回合事件，说明 DSH 没有把事件发过来（检查 DSH 是否真的重启过）。

## 自检

不需要 DSH、也不需要 BalancePet 就能验证插件的契约与容错：

```powershell
node tools\dsh-bridge\dsh-bridge-selftest.mjs tools\dsh-bridge\lib\index.js
```

自检会自动开启 `DSH_BRIDGE_DRY_RUN`：**它不会向真实管道发送任何东西**。否则 BalancePet 会把这些合成回合当成真实用量记进使用记录。
覆盖模块导出形状、`apply()` 对畸形 `ctx` 的容错、监听器对畸形事件的容错，以及 `turn/end` 是否复用 `turn/start` 的回合 id。

## 已知限制

- **更新插件后必须重启 DeepSeek Harness。** 首次安装会被自动热加载（新增挂载项会触发重新组合），但替换磁盘上的模块文件不会：ESM 模块在 DSH 进程内按 URL 缓存，只有进程重启才会重新导入。重跑安装脚本只是更新了文件，要重启 DSH 才会用上新代码。
- 依赖 DSH 的内部状态与事件契约。DSH 升级后若事件改名，插件会静默收不到事件（日志停在 `bridge active`），不会影响 DSH 本身。
- 若 DSH 在回合中途被强杀，`turn/end` 不会到达，桌宠会停在「工作中」。这是所有上报客户端的共性问题，不是本插件特有。
- 用量取自 DSH 的会话投影缓存，而该缓存在 checkpoint 才落盘，所以刚结束的回合可能读到略旧的总量；差额会计入下一个回合，不会丢失。
- 只上报 token 与模型，**从不上报费用**：DSH 只数 token、没有价格表。走中转站时由 BalancePet 自己的逐条日志回填费用；走官方账户时不存在按次计费数据，显示「未上报」是正确行为。
- 升级 BalancePet 不会更新 profile 里的插件副本，需要重新运行安装脚本。
