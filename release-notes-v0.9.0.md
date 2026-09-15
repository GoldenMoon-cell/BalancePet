# BalancePet v0.9.0

## 新增

- 扩充桌宠随机彩蛋：按形象、触摸部位、闲置状态和连续互动提供独立候选池，并避开最近使用的台词。
- 启用“读取 CC Switch 当前账户”后，只读监听 CC Switch 当前供应商；切换 CC Switch 账户时自动匹配并切换 BalancePet 本地账户。
- Balance Usage v1 摘要增加当前账户余额快照，供可信功能扩展显示总余额。

## 修复与改进

- CC Switch 账户识别只在本机内存中计算令牌 SHA-256 指纹，不保存、不记录、不上传 API 令牌。
- 移除不会被普通客户端触发的命名管道登录上报机制，登录状态统一来自 CC Switch。

## 规范与兼容

- 主程序版本：`v0.9.0`。
- 功能扩展接口继续使用 `api_version: 1`；Balance Usage v1 的 `balances` 字段为可选扩展，旧插件可忽略。
- 运行时依赖 Microsoft.Data.Sqlite，仅用于只读访问本机 CC Switch 数据库。

## 注意

- CC Switch 集成需要启用“读取 CC Switch 当前账户”，并使用默认 `%USERPROFILE%\\.cc-switch\\cc-switch.db` 数据目录。
- 若 CC Switch 账户未配置到 BalancePet，桌宠仍会提示在设置中添加；BalancePet 不读取网页登录 Cookie 或向外部服务发送凭据。
- `WORK_STATUS.md` 是本地工作记录，不随本版本提交或发布。

## 文件校验

- `BalancePet-0.9.0-win-x64.zip` SHA-256：`c4e777643b55dd6a651affdf8d61de5ba79893e1dbfd501c01e31ee5697655f7`
- `BalancePet-0.9.0-Setup.exe` SHA-256：`72d993fcb4507eaf763da8692d172fbcd6f11214f3f9e9575bace5c975136e14`
