# BalancePet v0.9.1

## 修复

- 移除不会被普通客户端触发的 `BalancePet.Account.v1` 命名管道和旧上报脚本。
- 登录状态来源统一为 CC Switch 当前供应商数据库，避免旧机制长期没有任何触发。
- 只读取 CC Switch 中 `app_type=codex` 的当前账户，避免把 Claude 等其他客户端账户误报为 Codex API。
- 未匹配的中转站账户显示 CC Switch 账户名和实际接口地址，不再显示成“Claude API”。
- 保留旧设置 JSON 键名，已有配置升级后仍能继续使用 CC Switch 读取功能。

## 其他

- 更新设置界面、打包脚本和使用文档，明确登录状态来自 CC Switch。
- 发布到 BalancePet GitHub Release。

## 文件校验

- `BalancePet-0.9.1-win-x64.zip` SHA-256：`74f18bbcf40e15c16d03d36f2ae29a787e58b24efd0c99d9b9dfa95a75353bdc`
- `BalancePet-0.9.1-Setup.exe` SHA-256：`c2be0917081ecfe949f3f174b0e01c79aad83d118ce94a9a7984d8f905530c61`
