# BalancePet v1.2.0

## 主要更新

- 浏览器会话桥接支持持续同步：首次配对有效期为 10 分钟，每次同步成功自动续期 10 分钟；只有连续 10 分钟无法同步才会失效。
- 浏览器桥接扩展单独发布为 `BalancePet-BrowserBridge` 仓库，版本为 `v1.0.0`。
- 在线插件库新增浏览器扩展类型。浏览器扩展仅显示“打开仓库”，不会被主程序当作可安装的功能、主题或资源扩展。
- 修复浏览器会话读取器中 Edge/Chrome 选择保存后重新打开仍显示 Edge 的问题。
- 用量记录和浏览器会话同步可以在设置窗口关闭后继续接收。

## 发布内容

- 主程序：`BalancePet-1.2.0-Setup.exe`、`BalancePet-1.2.0-win-x64.zip`
- 用量统计：`BalancePet-Ext-Feature-UsageAnalytics v0.3.10`
- 消息中心：`BalancePet-Ext-Feature-NotificationCenter v0.5.15`
- 云母主题：`BalancePet-Ext-Theme-Mica v1.0.0`
- 浏览器桥接：独立仓库 `BalancePet-BrowserBridge v1.0.0`
