# tbt-http-bridge

> ThinkBook Toolkit 插件（API v1）：把 1.1.0 移除的**「与其它软件联动」本机 HTTP 接口**做回来。

[![Release](https://img.shields.io/github/v/release/2006sila/tbt-http-bridge?display_name=tag)](https://github.com/2006sila/tbt-http-bridge/releases)
[![License](https://img.shields.io/badge/license-MPL--2.0-blue)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows-0078d4)
![.NET](https://img.shields.io/badge/.NET-9.0-512bd4)
![TBT Plugin API](https://img.shields.io/badge/TBT%20Plugin%20API-v1-2ea44f)

**English quick start** — Restores the local HTTP integration endpoint that ThinkBook Toolkit 1.1.0 removed.
Requirement: a Toolkit build with the plugin system (Plugin API v1). Install: Plugins page → Import plugin → review 3 permissions → enable → pick mode/port.
Endpoints: `GET /` (sensor snapshot), `GET /status`, `POST /performance-mode | /fan-strategy | /fan-full-speed`. License: MPL-2.0.

---

## 这是什么，为什么有它

ThinkBook Toolkit 1.1.0 的更新日志写着：

> Replaced the built-in localhost HTTP data/control listener with the reviewed plugin APIs. Existing HTTP integration settings are ignored and **no listener is opened by Toolkit**.

也就是说：**1.0.4 的「设置 > 与其它软件联动」被删了**（默认端口 2975）。任何依赖它的脚本、看板、自动化都会在升级后直接失联。

本插件把那个接口以**插件**形式做回来，并且**保持契约逐字兼容**——旧客户端一行都不用改：

- 端口默认 2975，只绑定 `127.0.0.1`
- 路由、请求体、响应字段、错误文案、CORS 头与 1.0.4 一致
- 从 TBT v1.0.4 的 `LocalDataSharingService.cs` 移植（MPL-2.0），见 [docs/provenance.md](docs/provenance.md)

## 特性

- **契约兼容**：旧脚本、旧看板、旧 PowerShell 示例原样可用
- **权限最小**：只申请 `sensors.read` / `data.read` / `host.control`；不申请 `ui.custom`（不在主程序进程里跑自绘代码）
- **单 DLL 零依赖**：插件包只有 `plugin.json` + 一个 DLL + 一个 deps.json，不携带契约程序集
- **原生设置界面**：设置分组卡片 + 双语标题 + 说明 + 图标，深浅色/中英文跟随本体
- **默认关闭**：装好后不监听任何端口，要手动选模式
- **多一个只读 `/status`**：排障用，不影响旧客户端

## 截图

**插件卡片**（插件页：版本、作者、请求的 3 项权限、扩展内容）

![插件卡片](docs/images/plugin-card.png)

**设置页**（分组卡片 + 联动模式 + 监听端口）

![本机联动设置页](docs/images/plugin-page.png)

## 快速开始

**前置**：ThinkBook Toolkit ≥ 1.1.0（或任何带插件系统的构建，插件 API v1）。

1. 到 [Releases](https://github.com/2006sila/tbt-http-bridge/releases) 下载 `TbtHttpBridge-<版本>.zip`
2. Toolkit → **插件** 页 → **导入插件** → 选中该 zip（导入后默认**停用**）
3. 审核列出的 3 项权限 → **启用**
4. 左侧出现 **本机联动** 页：把 *联动模式* 设为 `仅共享数据`（或 `允许共享数据和调整部分设置`），*监听端口* 保持 `2975`（改完点该行右侧的 **应用**）
5. 验证：

~~~powershell
Invoke-RestMethod http://127.0.0.1:2975/        # 传感器快照
Invoke-RestMethod http://127.0.0.1:2975/status  # 插件状态
~~~

> Toolkit 不允许覆盖同 ID 插件，升级请看下一节。

## 升级与卸载

**升级**（1.0.0 → 1.1.0 等同 ID 更新）：

1. 插件页 → 本插件卡片 → **卸载**（插件设置会保留）
2. **导入插件** → 选新 zip → 审核权限 → 启用
3. 1.1.0 起下拉选项改为中文（`关闭` / `仅共享数据` / `允许共享数据和调整部分设置`）。
   若设置文件里还是旧版 ASCII 值，下拉会显示空白——**重新选一次模式**即可；不重选插件也照常工作（内部兼容两种写法）。

**卸载**：插件页 → 卸载。残留文件可按需手工清理：

| 内容 | 路径 |
|---|---|
| 插件本体 | `%USERPROFILE%\.thinkbook_toolkit\plugins\tbts.http-bridge\` |
| 插件设置 | `%USERPROFILE%\.thinkbook_toolkit\plugin-settings\tbts.http-bridge.json` |
| 日志与状态 | `%LOCALAPPDATA%\TbtHttpBridge\` |

## HTTP 接口参考

### `GET /` —— 传感器快照

任意模式下都可用（`仅共享数据` 也能读）。

~~~json
{
  "cpuTemperatureC": 60,
  "cpuPowerW": 31.61,
  "gpuTemperatureC": 37.75,
  "gpuPowerW": 8.99,
  "fan1Rpm": 6023,
  "fan2Rpm": 6125,
  "performanceMode": "Performance"
}
~~~

| 字段 | 单位 | 说明 |
|---|---|---|
| `cpuTemperatureC` / `gpuTemperatureC` | °C | 读数过期（stale）或不可用时为 `null` |
| `cpuPowerW` / `gpuPowerW` | W | 同上 |
| `fan1Rpm` | RPM | 同上 |
| `fan2Rpm` | RPM | **仅双风扇机型**返回，否则为 `null` |
| `performanceMode` | — | `PowerSaving` / `Intelligent` / `Performance` / `Geek`；未知时为 `null` |

### `GET /status` —— 插件状态（新增）

~~~json
{
  "mode": "share-data-and-control",
  "port": 2975,
  "listening": true,
  "lastError": null,
  "queued": 0,
  "dispatched": 4,
  "lastRoute": "fan-strategy",
  "lastValue": "FanCurve",
  "lastDispatchAt": "2026-10-09T12:39:43.2020904+08:00",
  "startedAt": "2026-10-09T12:35:26.6168564+08:00"
}
~~~

排障时先看 `listening` 与 `lastError`，再看 `dispatched` / `lastRoute` 是否随请求增长。

### 控制路由（需要 `允许共享数据和调整部分设置`）

请求体固定为 `{"value": ...}`：

| 路由 | 取值 |
|---|---|
| `POST /performance-mode` | `PowerSaving` / `Intelligent` / `Performance` / `Geek`（大小写不敏感） |
| `POST /fan-strategy` | `FirmwareAutomatic` / `FixedRpm` / `FanCurve` / `AdvancedCurve` |
| `POST /fan-full-speed` | `true` / `false` |

成功响应：

~~~json
{ "success": true, "error": null, "queued": true }
~~~

> ⚠️ **`queued: true` 的含义**：请求已通过本地校验并**交给 Toolkit 执行**，**不等于硬件已确认**。
> Toolkit 不向插件回报执行结果（这是宿主的契约）。想确认是否生效，请稍后重新 `GET /` 对比读数
> （例如 `performanceMode` 是否变化）。

### 错误码与文案（与 1.0.4 逐字一致）

| 情况 | 状态码 | `error` |
|---|---|---|
| 模式为 `仅共享数据`（控制未开启） | 403 | `Control requests are disabled.` |
| 未知路由 | 404 | `Unknown control route.` |
| 请求体缺少 `value` | 400 | `JSON property 'value' is required.` |
| 性能模式非法 | 400 | `Invalid performance mode. Use PowerSaving, Intelligent, Performance, or Geek.` |
| 风扇策略非法 | 400 | `Invalid fan strategy. Use FirmwareAutomatic, FixedRpm, FanCurve, or AdvancedCurve.` |
| 全速取值非法 | 400 | `Invalid fan-full-speed value. Use true or false.` |
| 请求体超过 64 KiB | 413 | `Request body is too large.` |
| 非 GET/POST | 405 | （空） |
| 宿主未在一轮刷新内取走命令 | 503 | `The toolkit host did not dispatch the request in time.` |

`OPTIONS` 返回 204；所有响应带 `Access-Control-Allow-Origin: *`、`Cache-Control: no-store`。

> 注意执行顺序：**模式检查先于路由校验**。所以在 `仅共享数据` 模式下，连未知路由也返回 403（与 1.0.4 相同）。

## 与 1.0.4 的差异

| 差异 | 说明 |
|---|---|
| 传输层 | 由 `HttpListener` 换成 `TcpListener` + 最小 HTTP/1.1。`HttpListener` 依赖 http.sys 的 URL 保留（非管理员会 `Access denied`，实测本机 `HttpListener.IsSupported == false`）；`TcpListener` 没这个依赖，宿主是否提权都能用 |
| 过期读数 | `stale` / `unavailable` 的传感器读数返回 `null`，不再返回过期旧值（旧版只过滤非有限数） |
| 响应字段 | 成功响应**新增** `queued`（`success` / `error` 不变） |
| 新增路由 | 只读 `GET /status` |

其余（路由、取值、状态码、错误文案、CORS、端口默认值、仅回环、无鉴权）与旧版一致。

## 工作原理（给要改它的人）

~~~
Toolkit 主程序（requireAdministrator，独立 UI 进程）
  └─ PluginHost 工作进程 ← 每秒调用一次 EvaluateAsync
       ├─ TcpListener 线程池：127.0.0.1:<port>
       │    ├─ GET  /        从最近一次 Context 构造快照，立即返回
       │    ├─ GET  /status  插件状态
       │    └─ POST /<route> 本地校验 → 入队 → 等宿主取走（≤3s）
       └─ 队列 → PluginResult.Commands（每轮 ≤4 条）→ 宿主反射执行
~~~

控制链路经过**三重前置**，因为宿主对命令很严格——**命令执行抛异常或一次返回超过 8 条命令，插件会被挂起**：

1. **本地白名单**：取值必须命中枚举集合（错误文案与旧版一致）；
2. **能力门禁**：`Context.Data.Features` 里 `performance.its` / `performance.fan` / `performance.fan.full-speed` 标记为不可用时直接拒绝（报告缺失则放行，保持旧行为）；
3. **动态解析宿主操作**：命令名与参数名从 `Context.Operations` 里取（`SetItsModeAsync(mode)`、`SetFanModeAsync(mode)`、`SetFullSpeedAsync(enabled)`），解析不到就不下发。

另外两条硬约束：

- `EvaluateAsync` **绝不抛异常**（抛了插件会被挂起，端口随之消失）；
- 插件目录**只读**：宿主对插件目录做「相对路径 + 文件内容」的 SHA256 指纹，目录一变就必须重启重新授权，所以日志与状态一律写在 `%LOCALAPPDATA%\TbtHttpBridge\`。

## 权限说明

| 权限 | 用途 | 为什么需要 |
|---|---|---|
| `sensors.read` | 读内置传感器字典 | `GET /` 的温控、功耗、风扇转速 |
| `data.read` | 读运行时快照 | `performanceMode` 来自快照里的 `ItsMode`；能力门禁来自 `Features` |
| `host.control` | 提交宿主操作 | 三条 POST 路由实际执行的入口 |

**没有**申请：`ui.custom`（不写自绘页面，避免在主进程里跑插件代码）、`ui.toast`（不发提示）、`replace`（不替换任何内置页面/设置/传感器）。

## 已知限制与安全

- **Toolkit 不运行，端口就不监听**（插件随工作进程生命周期，与旧版内置实现相同）；
- 接口**没有鉴权**（与旧版一致）：只绑定回环地址，但同机任意进程都能调用。**默认关闭**，用完建议切回 `关闭`；
- 控制类请求是否被硬件接受，取决于当前机型的 Toolkit 能力，插件只做前置校验；
- 该插件以宿主权限运行（受信赖代码模型，**不是安全沙箱**），只启用你审阅过的构建产物；
- 非官方项目：与联想无关，也不属于 ThinkBook Toolkit 官方插件；使用风险自担（与上游免责声明同调）。

## 构建与测试

**前置**：.NET 9 SDK（Windows）。不需要预装 NuGet 包，也不需要预装 Toolkit——契约源码已随仓库提供（`third_party/PluginApi`，MPL-2.0）。

~~~cmd
build.cmd      :: dotnet msbuild -t:Restore,Build -m:1
test.cmd       :: 构建并运行 HostSim 夹具（39 项断言，离线可跑）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack.ps1 -Version 1.1.0
~~~

- 为什么必须 `-m:1`：多 ProjectReference 的项目在并行还原/构建下会**静默失败**（退出码 1、无错误、无产物）。
- 想用本机宿主自带的契约（保证与你的 Toolkit 版本完全一致）：`tools\fetch-contract.ps1` 会把
  `ThinkBookToolkit.PluginApi.dll` 复制到 `lib\`，构建时优先使用它（`lib/` 不进版本库）。
- 契约程序集**永远不打包**：它对宿主是同一程序集标识，随包分发会与宿主已加载的实例冲突。

### HostSim 夹具覆盖（39 项）

| 组 | 内容 |
|---|---|
| 加载 | 按 `PluginHost` 的方式（`AssemblyLoadContext` + `AssemblyDependencyResolver` + 契约回落）加载**打包后的** DLL |
| 快照 | 7 字段集合/类型、`stale` 与不可用 → `null`、`ItsMode` 数字 → 名称、无第二风扇 |
| 控制 | 三条路由下发内容（命令名/参数名/JSON 值类型）、大小写规范化、每轮 ≤8 |
| 错误 | 403/404/400/405/500/503 与逐字文案 |
| 门禁 | 能力不可用 → 拒绝；宿主不暴露命令 → 不发（避免挂起） |
| 生命周期 | 端口切换、停用释放、`Dispose` 释放 |
| 健壮性 | 队列上限 503、洪泛后存活、畸形流量与裸 TCP 垃圾 |

> ⚠️ **夹具端口固定为 3299 / 3399**。不要改成 2975：那会连到你正在运行的真实插件上，洪泛用例会把控制命令打进真机硬件。
> 夹具启动后还会自检快照是否等于自己的假数据，不是就立即中止。

## 打包与发布

`tools\pack.ps1` 只收集三个文件（白名单）：`plugin.json`、`TbtHttpBridge.dll`、`TbtHttpBridge.deps.json`，并打印各自 SHA256。

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0          # dry run
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -Execute # 发布
~~~

**可复现性**：`Directory.Build.props` 固定了 `PathMap`，同一提交在任意目录/机器构建都会得到逐字节相同的 DLL；
**ZIP 不可复现**（`Compress-Archive` 会写入文件时间戳）。所以 Release 的校验口径是「包内 DLL 的 SHA256」，不是整包哈希。

> v1.1.0 的 Release 资产构建于加入 `PathMap` 之前：其 DLL SHA256 为 `5760BC8EB4AD4974E90F6C2082845958178E6084DDEE7BC986B179D80436D506`，
> 而由当前源码构建会得到 `C340A6ADB3FB4B663C2234F2846F70961AA6CD2DC537DA70EDC8F0667CD9F120`（仅嵌入路径不同，功能一致）。

## 排障

| 现象 | 处理 |
|---|---|
| `curl` 连不上 | 先看插件页模式是不是 `关闭`；再看 `%LOCALAPPDATA%\TbtHttpBridge\bridge.log`；Toolkit 必须正在运行 |
| 概览里「本机联动」显示端口不可用 | 端口被占用。换端口或释放占用者；插件每 10 秒自动重试 |
| 插件页显示插件出错 | 宿主拒绝了下发的命令并挂起插件。日志里有最后下发的命令；在插件页重新启用即可 |
| 下拉框显示空白 | 设置文件里是旧版 ASCII 值（1.0.0 → 1.1.0 升级后），重新选一次模式 |
| POST 返回 503 | 宿主在 3 秒内没取走命令（插件刚启用），或队列满（256） |
| 导入被拒 | Toolkit 不允许覆盖同 ID 插件：先卸载，再导入 |
| `/status` 里 `lastError` 非空 | 监听失败原因原文（多为端口占用/权限），插件会继续重试 |
| 想先确认插件没在乱写文件 | 插件目录只会是 `plugin.json + dll + deps.json`，运行期字节不变 |

## 目录结构

~~~
tbt-http-bridge/
├─ plugin.json                     插件清单（API v1）
├─ src/TbtHttpBridge/              插件本体（net9.0，无外部包依赖）
│   ├─ HttpBridgePlugin.cs         IToolkitPlugin：设置同步 / 队列 / 命令下发
│   ├─ LocalDataSharingService.cs  1.0.4 移植：HTTP 契约与传输层
│   ├─ BridgeState.cs              Context → 快照
│   ├─ BridgeSettings.cs           设置解析（兼容新旧模式值）
│   ├─ ControlRules.cs             取值校验 / 能力门禁 / 宿主操作绑定
│   ├─ ControlQueue.cs             控制请求队列
│   └─ BridgeLog.cs                日志与 status.json（写在 %LOCALAPPDATA%）
├─ tests/HostSim/                  离线宿主夹具（不进插件包）
├─ tools/                          pack.ps1 / release.ps1 / fetch-contract.ps1
├─ third_party/
│   ├─ TBT-1.0.4-LocalDataSharingService.cs   上游原文（用于 diff）
│   └─ PluginApi/                  上游插件契约源码（MPL-2.0，仅编译期）
├─ docs/provenance.md              移植来源与逐处改动
└─ build.cmd / test.cmd
~~~

## 许可与致谢

- 本仓库整体以 **Mozilla Public License 2.0** 发布，见 [LICENSE](LICENSE)。
- `src/TbtHttpBridge/LocalDataSharingService.cs` 移植自
  [ThinkBookToolkit](https://github.com/lhzlhz419/ThinkBookToolkit) 的 `src/ThinkBookToolkit/LocalDataSharingService.cs`（tag **v1.0.4**，
  SHA256 `D9CD4E619E7DA64A026174D12E3214FBB440063EE9A6180A4103245249E45BA2`），保持 MPL-2.0；
  上游原文留存在 `third_party/`，逐处改动见 [docs/provenance.md](docs/provenance.md)。
- `third_party/PluginApi/Contracts.cs` 取自同一上游仓库（main，插件 API v1），仅用于编译期。
- 感谢 **lhzlhz419** 与 ThinkBook Toolkit 的作者 **咕咕咕---TB大王**。
- 本项目与联想、ThinkBook Toolkit 官方均无隶属关系。

## 变更记录

见 [CHANGELOG.md](CHANGELOG.md)。最近两个版本：

- **1.1.0** —— 设置分组卡片、中文模式选项、作者署名。
- **1.0.0** —— 首个版本：恢复 1.0.4 的本机 HTTP 接口。
