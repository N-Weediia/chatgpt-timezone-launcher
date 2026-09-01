# ChatGPT 时区启动器 v1.1.0

Windows x64 自包含单文件版本，无需安装 .NET 或管理员权限。

## 下载与使用

1. 下载 Release 附件 `ChatGPT-TimeZone-Launcher-v1.1.0-win-x64.exe`。
2. 选择“自动跟随 ChatGPT 实际出口”或“手动选择时区”。
3. 点击“保存并启动 ChatGPT”。
4. 如需停用，点击“恢复 ChatGPT 默认启动方式”。

## 核心功能

- 只为本次启动的 ChatGPT 进程及子进程设置 `TZ`，不修改 Windows 系统时区。
- 自动探测 ChatGPT/OpenAI 域名流量的实际出口，并转换为 IANA timezone。
- 支持搜索完整 IANA 时区列表。
- 动态定位 Microsoft Store ChatGPT AppX/MSIX 包及实际入口。
- ChatGPT 已运行时明确提示，可由用户确认后请求正常关闭重启。
- 一键恢复标准 AppX 启动，不修改注册表、ChatGPT 文件或用户数据。

## 1.1.0 主要改进

修复规则分流环境下，GeoIP 服务自身走 Default Proxy、导致自动时区与 ChatGPT 实际出口不一致的问题。

新流程：

```text
ChatGPT/OpenAI /cdn-cgi/trace
→ ChatGPT 实际出口 IP
→ 查询这个明确 IP 的 GeoIP
→ IANA timezone
→ 进程级 TZ 启动 ChatGPT
```

不依赖 Clash/Mihomo API，也不根据节点名称猜地区。支持 ChatGPT/OpenAI trace 多域名 fallback，以及 IPinfo、ipapi.co、ipwho.is 多 GeoIP fallback。

## 借鉴说明

功能构想和基础交互参考了 Opus94 分享的“ChatGPT 时区启动器”截图与说明。本项目为从零独立实现，没有复制参考工具源码或二进制；自动出口检测、包定位、配置、界面和测试均重新设计。

## 验证

- 自动化测试：20/20 通过。
- 实际规则分流验证：Default Proxy 为台湾时，ChatGPT trace 正确识别美国出口并得到 `America/Los_Angeles`。

## 注意

- 当前附件为 Windows x64 版本。
- EXE 未进行商业代码签名，Windows SmartScreen 可能显示提示。
