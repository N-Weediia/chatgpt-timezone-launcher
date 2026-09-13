# ChatGPT 时区启动器

一个独立、免安装的 Windows 小工具。它不会修改 ChatGPT、Windows 系统时区、注册表或全局环境变量；只在启用时区覆盖时，为这一次新建的 ChatGPT 进程及其子进程设置 `TZ=<IANA timezone>`。

## 项目来源与致谢

本版本基于 **404elf** 发布的 [`chatgpt-timezone-launcher`](https://github.com/404elf/chatgpt-timezone-launcher) 源码修改而来。感谢 404elf 对 ChatGPT 进程级时区覆盖思路、AppX/MSIX 定位和原始启动器实现所做的工作。

本次修改主要针对 WindowsApps 直接执行时的“拒绝访问”问题，改用 AppX 激活后进行进程级环境覆盖，并增加启动窗口前置处理。原项目的 MIT License 和版权声明保留在本仓库中。

## 灵感来源与借鉴说明

本项目的功能构想和基础交互，参考了 **Opus94 分享的“ChatGPT 时区启动器”截图与使用说明**。感谢原作者验证了“仅向 ChatGPT 进程传入 `TZ`、不修改 Windows 系统时区”这一思路的可行性。

原始仓库的功能构想和实现是本版本的基础；本版本只在此基础上进行明确记录的兼容性修复和功能改进，不包含原作者未声明的归属变更。

相较参考方案，本项目主要增加或改进了：

- 自动检测 **ChatGPT/OpenAI 流量实际使用的出口**，而不是普通网络或 GeoIP 服务自身的出口；
- 兼容 Clash/Mihomo 规则分流，但不依赖 Clash、节点名称或 External Controller API；
- `chatgpt.com/cdn-cgi/trace → 明确出口 IP → 指定 IP GeoIP → IANA timezone` 两阶段检测；
- ChatGPT/OpenAI trace 多域名 fallback，以及 IPinfo、ipapi.co、ipwho.is 多 GeoIP fallback；
- 动态解析当前用户的 ChatGPT AppX/MSIX 包、版本和实际入口，不写死 WindowsApps 路径；
- 通过 AppX 激活拿到新进程 PID，暂停后更新该进程的环境块，再恢复运行，避免直接执行 WindowsApps 文件的拒绝访问；
- 完整 IANA 时区搜索、配置损坏恢复、旧版错误缓存迁移和一键恢复默认启动；
- 已运行检测与用户确认后的正常关闭重启，不默认强制结束 ChatGPT；
- 自包含单文件 EXE，以及覆盖规则分流、网络失败、包更新和进程环境的自动化测试。

> 如果你知道原分享内容的长期有效原始链接，欢迎提交 Issue 或 PR 补充更精确的出处。

## 使用

1. 从 GitHub Releases 下载 `ChatGPT-TimeZone-Launcher-v1.2.0-win-x64.exe`，放在任意普通目录后运行，无需安装和管理员权限。
2. 选择“自动跟随 ChatGPT 实际出口”或“手动选择时区”。
3. 点击“保存并启动 ChatGPT”。自动模式会在每次启动前重新联网检测，节点变化不会被旧缓存遮盖。
4. 如要停用覆盖，点击醒目的“恢复 ChatGPT 默认启动方式”。此时两个模式均不选中；之后点击“启动 ChatGPT（默认方式）”会使用标准 AppX 激活，不注入 `TZ`。重新点选任一模式即可再次启用。

ChatGPT 已运行时，进程内时区不能动态改变。启用覆盖后，启动器会明确提示，并可在你确认后请求 ChatGPT 正常关闭再重启；不会强制结束进程。

### 两种模式

- **自动跟随 ChatGPT 实际出口**：适合代理、TUN 和规则分流环境；每次启动前重新检测。
- **手动选择时区**：可搜索完整 IANA timezone，例如 `Asia/Shanghai`、`Asia/Tokyo`、`America/Los_Angeles`。

### 恢复默认

点击“恢复 ChatGPT 默认启动方式”后，启动器不再向 ChatGPT 注入 `TZ`。之后点击“启动 ChatGPT（默认方式）”等价于正常 AppX 启动；不会修改或删除 Windows 时区、ChatGPT 文件和用户数据。

### Codex 限额与托盘提醒

启动器打开后会读取当前用户已有的 Codex 登录会话，并请求官方 usage 接口显示：

- 5 小时窗口和 7 天窗口的已用百分比与剩余百分比；
- 按北京时间（UTC+8）显示的重置时间；
- 距离重置的动态倒计时；
- 重置前 10 分钟的系统托盘提醒。

限额数据只保存在内存中，不写入 `settings.json`。如果点击右上角 `×` 后选择隐藏到托盘，状态、限额刷新和提醒会继续运行；可从托盘菜单重新打开窗口或退出程序。若未找到 Codex 登录会话、会话过期或官方接口不可用，面板会显示失败原因，可手动重试。

## 自动时区与隐私

自动模式不会再把 GeoIP 服务请求自身的出口当成 ChatGPT 出口。规则分流下，`ipapi.co`、`ipinfo.io` 等域名可能命中 Default Proxy，而 ChatGPT 命中另一策略组；两者的调用方 IP 并不等价。

检测顺序：

1. 访问 `https://chatgpt.com/cdn-cgi/trace`，从 `ip=` 字段取得这条 ChatGPT 域名请求的真实出口；请求禁用缓存。
2. 主 trace 失败时，依次尝试 `api.openai.com`、`auth.openai.com`、`chat.openai.com` 上相同的 `/cdn-cgi/trace`。不跟随到其他域名的重定向。
3. 使用明确 URL `https://ipinfo.io/<ChatGPT出口IP>/json` 查询这个指定 IP。
4. IPinfo 失败后，依次使用 `ipapi.co/<IP>/json/` 和 `ipwho.is/<IP>`，仍然只查询同一个明确 IP。

第二步 GeoIP 请求自身经过哪个代理不再影响结果，因为返回 IP 必须与 trace IP 完全一致，否则整项结果会被拒绝。Cloudflare 将 `/cdn-cgi/trace` 定义为域名侧的网络路径诊断端点，见 [Cloudflare 文档](https://developers.cloudflare.com/fundamentals/reference/cdn-cgi-endpoint/)。

实现不依赖 Clash/Mihomo API，也不扫描或读取代理配置，因此 Clash 未运行、使用其他代理软件或无代理时同样可用。Mihomo External Controller 的端口和密钥均可自定义；为避免强绑定和读取敏感控制密钥，当前版本只将代理组、节点显示为“未读取”，不根据节点名称猜测地区。时区的最终依据始终是 ChatGPT trace IP 的 GeoIP。

trace 服务会看到 ChatGPT/OpenAI 请求的公网 IP；GeoIP 服务会收到要查询的明确 IP。启动器没有遥测，不会上传 ChatGPT 数据或配置。相关服务说明见 [IPinfo 文档](https://ipinfo.io/developers/ip-geolocation-api-data)、[ipapi 文档](https://ipapi.co/api/#location-of-clients-ip) 和 [ipwho.is](https://ipwho.is/)。如果 trace 或全部 GeoIP 查询失败，启动器不会猜测、不会回退普通出口，也不会用历史结果启动；上次成功结果只用于界面参考。

## ChatGPT 定位与启动

OpenAI 官方说明 Windows 客户端通过 Microsoft Store 分发，当前官方 Store 产品 ID 为 `9NT1R1C2HH7J`。启动器不写死 `C:\Program Files\WindowsApps` 路径，而是：

1. 查询当前用户的开始菜单 ChatGPT 入口和已注册 AppX/MSIX 包；
2. 读取已注册包清单中的 `InstallLocation`、`Application Id`、`Executable` 和 `Parameters`；
3. 对候选项评分，并优先选择版本号最新的 ChatGPT 入口；
4. 启用覆盖时通过 AppX 标准激活创建 full-trust 桌面入口，取得新进程 PID 后暂停它，仅在该进程环境块中加入 `TZ`，再恢复运行；
5. 默认方式使用 `shell:AppsFolder\<PackageFamilyName>!<AppId>` 标准激活，因此不会残留启动器注入值。

本机调研时检测到的当前包是 `OpenAI.Codex_26.825.6671.0_x64__2p2nqsd0c76g0`，清单入口为 `app/ChatGPT.exe`，`EntryPoint=Windows.FullTrustApplication`。这只是验证样本，不存在于代码常量中；Store 更新后的新版本目录会在每次启动时重新发现。参考：[OpenAI Windows 客户端说明](https://help.openai.com/en/articles/9982051)、[Microsoft 的 packaged desktop app 运行说明](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)、[MSIX 清单入口说明](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion)。

## 配置与恢复

配置位于：

```text
%LocalAppData%\ChatGPTTimezoneLauncher\settings.json
```

仅保存当前模式、手动时区、是否启用覆盖、上次成功检测、托盘关闭偏好和限额提醒偏好。限额本身及访问令牌不会写入配置。更新已有配置时先生成 `settings.json.bak`；新文件写完并反序列化验证后才替换旧文件。配置损坏时保留原文件并以安全默认状态启动。

“恢复默认”只把 `TimeZoneOverrideEnabled` 保存为 `false`。它不会修改 Windows 系统时区、系统/用户环境变量、注册表、ChatGPT 文件或用户数据；手动选择和历史检测结果会保留，方便以后重新启用。

## 从源码构建

要求 Windows 10/11 和 .NET 8 SDK。项目使用 WinForms 和 NodaTime（提供完整、可搜索且可验证的 IANA TZDB 列表）。在 PowerShell 中运行：

```powershell
.\build.ps1
```

脚本先运行测试，再发布 `win-x64`、自包含、压缩的单文件 EXE，然后在独立目录验证最终 EXE 的首次启动和损坏配置恢复。任一步失败会停止。输出：

```text
dist\win-x64\ChatGPT时区启动器.exe
```

## v1.2.0 小更新

当前源码版本为 **v1.2.0**。

- ChatGPT 启动后自动刷新“ChatGPT 进程”状态，并在窗口创建后尝试切到前台；
- 使用响应式表格布局，底部按钮和状态区域会随窗口宽度缩放，不再被固定宽度的页脚内容撑出横向滚动条；
- 点击右上角关闭按钮时可隐藏到系统托盘，后台继续刷新状态和限额；托盘菜单可重新显示或退出；
- 新增 Codex 5 小时和每周限额面板，显示已用/剩余百分比、北京时间（UTC+8）重置时间和倒计时；
- 支持手动刷新、接近重置前提醒，以及隐藏到托盘后继续提醒；
- 限额读取只在本机读取现有 Codex `.codex\\auth.json` 的访问令牌，并仅发送到 `chatgpt.com` 官方接口；令牌不写入启动器配置、日志或源码。

自动化测试 **23/23** 通过，发布 EXE 的首次启动及损坏配置启动检查均通过。

## v1.1.2 启动修复（历史）

针对部分 Windows 设备直接执行 `WindowsApps` 内 ChatGPT 文件时出现“拒绝访问”的问题，时区覆盖启动流程改为 AppX 激活后暂停新进程、更新进程专属环境块并恢复运行；该过程不修改系统时区、注册表或全局环境变量。

## v1.1.1 启动修复（历史）

修复损坏配置导致窗口创建前退出的问题，并增加启动异常日志和最终 EXE 的隔离启动检查。详细原因、验证边界和排错方法见 [v1.1.1 修复说明](docs/RELEASE_NOTES_v1.1.1.md)。

启动错误日志：`%LocalAppData%\ChatGPTTimezoneLauncher\logs`（不可写时尝试 `%TEMP%\ChatGPTTimezoneLauncher\logs`）。若无日志，请提供 Windows 版本、CPU 架构及系统错误提示，不能假定所有“无反应”都由同一原因引起。

## v1.1.0 历史验证结果

自动化测试 20/20 通过，覆盖：ChatGPT 美国出口与 Default 台湾出口分离、ChatGPT 节点切换、Default Proxy 切换、trace fallback/完全失败、指定 IP GeoIP fallback/完全失败、Clash 未运行、非 Clash 网络、旧版错误缓存迁移，以及原有配置、AppX 定位、手动时区、恢复默认和已运行分支。

本机人工验证：

- 旧逻辑实测得到 Default Proxy 的台湾出口；新逻辑实测 `chatgpt.com/cdn-cgi/trace` 返回 `134.195.101.58`（美国），指定 IP 查询得到 `San Jose, US` 和 `America/Los_Angeles`；
- 实际包清单动态解析成功，定位到当前 `OpenAI.Codex` 包的 `app/ChatGPT.exe`；
- 中文 GUI 在 100% DPI 下完成视觉检查；
- 本机 ChatGPT 当时正在承载当前任务，因此未执行“关闭并重启”的破坏性端到端测试。

## 已知限制

- 交付 EXE 为 Windows x64；ARM64 需要将构建运行时改为 `win-arm64` 后重新发布。
- GeoIP 的城市级定位由第三方数据库提供，可能存在误差；时区字段为空或不是有效 IANA ID 时会视为失败。
- 进程级方案依赖 ChatGPT 继续使用可由 AppX 激活的 packaged full-trust 桌面入口，并允许同一用户进程的短暂暂停和内存参数更新。若未来 Store 包改为纯 AppContainer/UWP，或系统策略禁止访问新进程的用户态参数，启动器会明确报错而不会修改系统设置绕过。
- 未签名的独立 EXE 可能触发 Windows SmartScreen 提示；源码构建本身不包含代码签名证书。
