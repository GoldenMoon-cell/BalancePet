# BalancePet v1.0.0

与上一正式版 [v0.9.1](https://github.com/GoldenMoon-cell/BalancePet/releases/tag/v0.9.1) 相比，`v1.0.0` 加入了完整的扩展体系，并继续完善账户、用量和桌宠工作流。

## 新增

- **用量统计扩展**：独立仪表盘和逐条使用记录，区分输入、输出、缓存读写 Token；额度只显示服务端实际报告值。
- **消息中心扩展**：提供本地通知记录和桌宠环绕信息，优先读取脱敏实时快照中的余额、任务与 CC Switch 登录状态。
- **New API 用量同步**：只读同一中转站的 Token 日志，补齐服务端逐次额度；匹配不到记录时仍显示“未上报”。
- **扩展库与主题**：支持功能、资源和主题扩展的安装、更新与卸载；新增 BalancePet Mica 主题扩展。

## 改进

- 用量统计自动刷新遵守主程序最短 30 秒刷新间隔，修复 Codex 延迟写入 Token 后历史记录缺失或重复的问题。
- 消息中心入口跟随扩展安装、启用和卸载状态更新；修复扩展重启后进程仍占用旧版文件的问题。
- 调整桌宠点击区域、静默刷新与彩蛋行为，避免重复气泡并正确显示手动刷新结果。
- 设置页、桌宠右键菜单和托盘菜单只列出具备完整九状态图片的 12 个内置形象；无素材角色保留在目录中，不显示为占位项。

## 扩展版本

- Usage Analytics `v0.3.3`
- Notification Center `v0.5.15`（随本次主程序 Release 提供）
- BalancePet Mica `v1.0.0`

## 文件校验

- `BalancePet-1.0.0-win-x64.zip` SHA-256：`af84eea3cb1f18af15ab4ac6241dc05d0ddfbeaa537b24965d67cd3d2635ffb1`
- `BalancePet-1.0.0-Setup.exe` SHA-256：`b24bcbece3c372473929f5195983a5fd1db794d56f8d3b44199428c0811d49e7`
- `balancepet.ext.feature.notification-center-0.5.15-win-x64.zip` SHA-256：`27eacc7554d30e8a5fa9fc252b0aaaf9b0bbbe833c079449cdde07f3836f88dd`
