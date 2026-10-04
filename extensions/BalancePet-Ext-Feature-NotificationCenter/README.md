# BalancePet 消息中心

[![下载量](https://img.shields.io/github/downloads/GoldenMoon-cell/BalancePet-Ext-Feature-NotificationCenter/total?label=%E4%B8%8B%E8%BD%BD%E9%87%8F&color=2ea043)](https://github.com/GoldenMoon-cell/BalancePet-Ext-Feature-NotificationCenter/releases)
[![最新版本](https://img.shields.io/github/v/release/=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&color=2ea043)](https://github.com/GoldenMoon-cell/BalancePet-Ext-Feature-NotificationCenter/releases/latest)
[![Stars](https://img.shields.io/github/stars/=Stars&color=2ea043)](https://github.com/GoldenMoon-cell/BalancePet-Ext-Feature-NotificationCenter/stargazers)

BalancePet 的功能扩展：把桌宠气泡里的脱敏摘要集中展示，并在悬停时显示环绕信息。

本仓库只发布扩展包与说明。扩展由主程序在扩展列表里安装，源码在主程序的
[`extensions/`](https://github.com/GoldenMoon-cell/BalancePet/tree/main/extensions/BalancePet-Ext-Feature-NotificationCenter)
目录下。

## 功能

**消息中心窗口**

左侧按类别导航：更新记录、任务、账户、余额、系统、全部消息。「互动」（点击与拖动
流水）不进入界面，但仍保留在事件流里供环绕信息取值。条目逐条从左进入；新消息
增量插入，不重建整个列表。

**未读**

未读属于那条消息，不属于正在看的列表：侧栏显示每类的未读数，离开该分类、点掉该条
或关闭窗口时清除；不在列表顶部时，顶部提示「N 条新消息」，点击回到顶部。

**环绕信息**

鼠标移到桌宠上并按住 `Shift`：余额、当前登录方式、任务状态与当前版本四个槽环绕显示。
信息块贴近桌宠边缘，按桌面背景自动选择浅色或深色文字与反向描边；松开 `Shift` 或移开
鼠标后按顺序淡出。

**跟随主程序外观**

窗口使用主程序的明暗模式、界面字体与配色；主程序在状态快照里发布这些值，扩展读取
后应用，改主题后一秒内同步。

**更新记录**

更新记录由主程序抓取，交给本扩展呈现；有新内容时桌宠会提一句，条目留在「更新记录」
分类里。

## 数据与隐私

扩展**只读**以下两个文件，且只读脱敏内容：

- `%LOCALAPPDATA%\BalancePet\notification-events.ndjson` 及轮转文件——消息记录
- `%LOCALAPPDATA%\BalancePet\notification-state.v1.json`——当前状态快照

记录中不含 API Token、提示词、模型响应、原始请求或原始响应。扩展不联网，也不写入
主程序的任何设置。

## 系统要求

Windows 10 1809（build 17763）或更高，64 位。随主程序发布，主程序核心版本需
不低于清单里声明的版本。

## 许可

见主程序仓库的 [LICENSE](https://github.com/GoldenMoon-cell/BalancePet/blob/main/LICENSE)。

