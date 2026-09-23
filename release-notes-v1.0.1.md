# BalancePet v1.0.1

`v1.0.1` 是 `v1.0.0` 的维护版本，重点修复程序内检查更新时可能误报 SHA-256 校验失败的问题。

## 修复

- 更新检查改用 GitHub Release 的专用资产接口读取当前 digest，避免 Release 列表缓存旧资产校验值。
- 保留便携 ZIP 与 Setup 安装器的下载、SHA-256 校验和目录权限处理逻辑。

## 文件校验

- `BalancePet-1.0.1-win-x64.zip` SHA-256：`82ce98ddc0f6e074003699d3f178f51690b511d87b2a4a5fbf323ab407802c8d`
- `BalancePet-1.0.1-Setup.exe` SHA-256：`e4feeddfcf1d51a9c389f7216c56107e4201b8b63fd133ab6858c613553f8113`
