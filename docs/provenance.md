# 移植来源与改动清单

## 来源

| 项 | 值 |
|---|---|
| 上游仓库 | https://github.com/lhzlhz419/ThinkBookToolkit （MPL-2.0） |
| 文件 | `src/ThinkBookToolkit/LocalDataSharingService.cs` |
| 版本 | tag **v1.0.4**（2026-09-06） |
| 上游文件 SHA256 | `D9CD4E619E7DA64A026174D12E3214FBB440063EE9A6180A4103245249E45BA2` |
| 本地留存副本 | `third_party/TBT-1.0.4-LocalDataSharingService.cs`（逐字节原文，便于 diff） |
| 移除证据 | CHANGELOG 1.1.0 *Changed*：*Replaced the built-in localhost HTTP data/control listener with the reviewed plugin APIs… no listener is opened by Toolkit.*；v1.1.0 文件树中该 .cs 已不存在 |
| 移植后文件 | `src/TbtHttpBridge/LocalDataSharingService.cs`，SHA256 `9812D6C6F5CA01B287EF4CFBF131D327BF4D079D8BDD335744E18EB539583124` |

### 编译期契约源码

| 项 | 值 |
|---|---|
| 来源 | 同一上游仓库的 `src/ThinkBookToolkit.PluginApi/Contracts.cs`（main 分支，插件 API v1） |
| 本地路径 | `third_party/PluginApi/Contracts.cs` |
| 本地 SHA256 | `C7C5AF71DE7D00FCC1C4672E7F88A18122E31B76584CAF960797BD7B38B001DC` |
| 用途 | **仅编译期**。运行时宿主提供同名同版本（`ThinkBookToolkit.PluginApi, Version=1.0.0.0`）程序集；插件包内绝不包含该程序集 |
| 备选 | `tools/fetch-contract.ps1` 可从本机已安装的 Toolkit 复制该 DLL 到 `lib/`，构建时优先使用，保证与本机宿主完全一致 |

## 保留不变的部分

- 路由与语义：`GET /` 快照字段（cpuTemperatureC / cpuPowerW / gpuTemperatureC / gpuPowerW / fan1Rpm / fan2Rpm / performanceMode，camelCase，非有限数为 null）；
  `POST /performance-mode`、`POST /fan-strategy`、`POST /fan-full-speed`，请求体 `{"value": …}`。
- 状态码与顺序：OPTIONS→204；GET→200；非 GET/POST→405；控制未开启→**403**（先于路由校验）；未知路由→404；缺 value→400；其余异常→500。
- 错误文案逐字一致（`Control requests are disabled.` / `Unknown control route.` / `JSON property 'value' is required.` / 三条 Invalid … 文案）。
- CORS 与缓存头：`Access-Control-Allow-Origin: *`、`Access-Control-Allow-Headers: Content-Type`、`Access-Control-Allow-Methods: GET, POST, OPTIONS`、`Cache-Control: no-store`。
- 取值规则：枚举解析大小写不敏感且限定可选集合（等价于旧的 `Enum.TryParse` + `PerformanceModeCycle.IsSelectableMode`）。
- 端口默认 2975、仅绑定回环地址、无鉴权（与旧版一致）。

## 改动清单

| # | 改动 | 原因 |
|---|---|---|
| 1 | 三个注入点改为插件侧实现：快照提供者 / 模式提供者 / 控制回调 | 原实现依赖 ToolkitRuntimeSnapshot、SoftwareIntegrationMode、ToolkitRuntimeService，插件不可见 |
| 2 | **传输层 HttpListener → TcpListener**（自带最小 HTTP/1.1 解析） | HttpListener 依赖 http.sys URL 保留：非管理员会 Access denied，实测本机 `HttpListener.IsSupported == false`。TcpListener 无此依赖 |
| 3 | 日志 `ToolkitLog` → `BridgeLog` | 插件不可用宿主日志；且**禁止写插件目录**（目录指纹会变），日志与状态写在 `%LOCALAPPDATA%\TbtHttpBridge\` |
| 4 | 新增只读 `GET /status` | 迁移期排障；不影响旧客户端（旧客户端只打 `/`） |
| 5 | 请求体上限 64 KiB、读取超时 5s、连接每请求关闭 | 防止畸形/超大请求拖死工作进程 |
| 6 | 去掉 `DeviceModelDetector.HasSecondFan()` | 宿主只在双风扇机型下发 `toolkit.sensor.fan2Rpm`，等价 |
| 7 | 控制结果语义 | 宿主执行结果对插件不可见，故 `success` 表示「已通过本地校验并交给宿主执行」，响应新增 `queued: true`；`/status` 暴露最近一次下发 |
| 8 | 模式取值兼容中文与旧 ASCII 两种写法 | 1.1.0 把下拉选项改成中文，升级前写入的设置文件里是 ASCII 值 |

## 数值来源差异（唯一有意的行为差异）

旧版快照直接透传运行时数值（仅过滤非有限数）；插件版通过 `Context.Sensors` 取值，
**`Quality != "valid"`（stale / unavailable）的读数返回 null**，不返回过期旧值。
