# Usage Analytics v0.2.9

## 新增

- 趋势图增加 Input、Output、Cache Creation、Cache Read 和 Cache Hit Rate 五个系列。
- 使用 BalancePet 图标作为统计窗口和任务栏图标。
- 遵循 BalancePet Feature Extension API v1，支持独立进程和独立更新。

## 修复与改进

- 统一缓存命中率口径为 `Cache Read / Input`，与事件规范和中转站统计口径一致。
- 改进仪表盘、使用记录、提供方与模型三个导航视图，以及滚动时的侧边栏高亮。
- 改进自动刷新倒计时、无数据提示和最近使用记录展示。
- 未上报的 Token、缓存、模型或耗时字段保持未上报，不伪造默认数值。

## 规范与兼容

- 插件版本：`v0.2.9`；接口版本仍为 `api_version: 1`。
- 要求 BalancePet 核心版本 `v0.7.7` 或更高版本。
- 插件 UI、主题、Logo、字体和窗口布局不属于主程序兼容性要求。

## 注意

- 插件读取的是脱敏 Usage Event v1 文件，不读取 Token、提示词、回复或其他凭据。
- 只有客户端主动上报 Token 和缓存字段时，仪表盘才能显示对应数据。

## 文件校验

- `balancepet.ext.feature.usage-analytics-0.2.9-win-x64.zip` SHA-256：`ee8de6360ecdefcbb11af2b5be8ac1f19441db1451e4ae79ca68d8fbe9d134a9`
