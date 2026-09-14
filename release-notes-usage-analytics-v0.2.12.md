# Usage Analytics v0.2.12

## 新增

- 新增“今日消费”卡片，读取 BalancePet 主程序提供的脱敏 Balance Usage v1 摘要。
- “今日消费”跟随主程序当前账户，并在摘要文件变化时刷新。

## 修复与改进

- 移除客户端无法稳定提供的平均首 Token 延迟卡片，避免显示误导性数据。
- 补充 Balance Usage v1 的数据来源、估算口径、隐私边界和中英双语使用说明。

## 规范与兼容

- 插件版本：`v0.2.12`；接口版本仍为 `api_version: 1`。
- 要求 BalancePet 核心版本 `v0.7.7` 或更高版本。
- 插件 UI、主题、Logo、字体和窗口布局不属于主程序兼容性要求。

## 注意

- “今日消费”只统计两次成功余额查询之间的余额下降，不代表中转站账单或每次请求的精确价格。
- 插件只读取脱敏用量事件和余额摘要，不读取 Token、提示词、回复、Cookie 或提供方网站。

## 文件校验

- `balancepet.ext.feature.usage-analytics-0.2.12-win-x64.zip` SHA-256：`8d3bc8baeef0015d660aee3722abdecb85552a99b410135f8dd6ce37cf2af22a`
