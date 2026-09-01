# 测试结果

测试日期：2026-09-01  
系统：Windows 11 x64，.NET SDK 8.0.424

## 自动化

```text
PASS  IANA timezone validation
PASS  config save/load and backup
PASS  corrupt config safely defaults
PASS  legacy self-exit cache is discarded
PASS  Cloudflare trace parser
PASS  ChatGPT US route wins while default route is TW
PASS  ChatGPT node switch is re-detected
PASS  default proxy switch cannot change ChatGPT result
PASS  OpenAI trace endpoint fallback
PASS  explicit-IP GeoIP provider fallback
PASS  explicit-IP GeoIP total failure
PASS  ChatGPT trace total failure never uses default exit
PASS  Clash not running does not block trace detection
PASS  non-Clash network works without controller API
PASS  AppX manifest candidate and version selection
PASS  AppX not installed diagnostics
PASS  manual TZ is process-local
PASS  restore/default launch has no TZ injection
PASS  default activation allows an existing ChatGPT instance
PASS  TZ override refuses an existing ChatGPT instance

20/20 tests passed
```

## 人工与本机集成检查

| 场景 | 结果 |
|---|---|
| 旧版普通出口检测 | 复现；GeoIP 自检测返回台湾 Default Proxy |
| ChatGPT trace | 通过；`chatgpt.com/cdn-cgi/trace` 返回美国出口 `134.195.101.58` |
| 指定 IP GeoIP | 通过；IPinfo 查询该 IP 返回 `San Jose, US`、`America/Los_Angeles` |
| 规则分流 | 通过；模拟 ChatGPT→US、Default→TW，自动时区保持美国 |
| 节点切换 | 通过；ChatGPT 节点切换会改变结果，Default Proxy 切换不会改变结果 |
| Clash/Mihomo 依赖 | 无；Clash 停止和非 Clash 网络测试均通过 |
| ChatGPT 当前包定位 | 通过；动态读取 `OpenAI.Codex` 26.825.6671.0 的 `app/ChatGPT.exe` |
| ChatGPT 更新路径变化 | 通过；模拟两个版本，选择新版本及新相对入口 |
| 中文 GUI | 通过；控件、状态、恢复按钮与诊断信息可见 |
| 已运行检测 | 通过；自动化使用现有测试进程验证分支 |
| 真实关闭并重启 ChatGPT | 未执行；ChatGPT 正在承载当前 Codex 任务，避免中断和数据风险 |

恢复默认分支使用标准 AppX AUMID 激活，测试确认启动参数未加入自定义 `TZ`；程序从未写入全局环境变量或注册表。
