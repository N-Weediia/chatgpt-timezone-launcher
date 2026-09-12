# v1.1.2 — AppX 激活与进程级时区覆盖

本版本基于 404elf 的原始 `chatgpt-timezone-launcher` 源码修改而来，感谢原作者的工作。MIT License 和版权声明保留。

## 修复

- 不再直接执行 `C:\Program Files\WindowsApps\...\ChatGPT.exe`，避免受保护的 WindowsApps 路径返回“拒绝访问”。
- 时区覆盖模式改用 Windows AppX 激活接口启动 ChatGPT。
- 激活接口返回新进程后，启动器会暂停该进程，在其进程专属环境块中写入 `TZ=<IANA timezone>`，再恢复运行。
- 启动后等待主窗口创建并尝试切到前台，避免 ChatGPT 已启动但窗口留在后台。

## 边界

- 仅支持 x64 Windows，与当前 ChatGPT x64 包匹配。
- 只更新本次 ChatGPT 进程及其后代继承的环境，不修改 Windows 系统时区、注册表、用户环境变量或 ChatGPT 数据。
- ChatGPT 已运行时仍需先正常退出；启动器不会强制结束现有进程。
- 如果未来 ChatGPT 改为不支持 AppX 激活的入口，启动器会报告失败，不会退回修改全局设置的方案。

## 验证

- 自动化测试：22/22 通过。
- 发布 EXE：全新配置和损坏配置的隔离启动检查通过。
