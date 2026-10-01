# v1.2.1 — 图标与 ChatGPT 主窗口启动修复

## 更新内容

- 使用像素风人物时区图标作为 EXE 和系统托盘图标。
- 修复 AppX 激活返回引导进程 PID，但真正的 ChatGPT 主窗口由另一个 `ChatGPT.exe` 子进程创建时，启动器误报“窗口尚未创建”的问题。
- 启动器现在会在同一 ChatGPT 安装目录下扫描所有相关进程，找到拥有主窗口的进程后自动恢复并前置。

## 验证

- 源码已推送到个人仓库 `N-Weediia/chatgpt-timezone-launcher`。
- 发布 EXE 使用自包含 Windows x64 构建。
