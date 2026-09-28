# BalancePet Browser Bridge v1.0.0

- 首次独立发布 Edge/Chrome 浏览器扩展。
- 通过本机配对码将当前已登录中转站会话传给 BalancePet。
- 每 30 秒增量同步新捕获的用量响应。
- 配对码初始有效期 10 分钟，每次同步成功自动续期 10 分钟。
- Cookie、网页会话和用量响应只通过 `127.0.0.1` 发送给本机 BalancePet，不经过公网。
