## 本机 HTTP 联动接口，回来了

ThinkBook Toolkit 1.1.0 移除了内置的「与其它软件联动」本机 HTTP 接口（默认端口 2975）。本插件把它以插件形式做回来，
**HTTP 契约与 1.0.4 逐字一致**：旧脚本、旧看板、旧 PowerShell 示例一行都不用改。

### 安装

1. 解压/直接使用本页附件 `TbtHttpBridge-1.1.0.zip`
2. Toolkit → **插件** 页 → **导入插件** → 选中 zip（导入后默认停用）
3. 审核 3 项权限（`sensors.read` / `data.read` / `host.control`）→ **启用**
4. 打开左侧 **本机联动** 页：选联动模式（默认 `关闭`），端口保持 2975 → 点「应用」
5. 验证：`Invoke-RestMethod http://127.0.0.1:2975/`

### 本版新增

- 设置改为原生**分组卡片**（标题 + 说明 + 图标），跟随本体主题与语言
- **联动模式改为中文选项**：关闭 / 仅共享数据 / 允许共享数据和调整部分设置
- 新增只读 `GET /status`（监听状态、队列、最近一次下发，排障用）
- 作者署名

### 兼容性与限制

- 需要带插件系统的 Toolkit 构建（插件 API v1；1.1.0 正式版适用）
- 默认**不监听**；接口只绑 `127.0.0.1`，且**没有鉴权**（与旧版一致），用完建议切回「关闭」
- `queued: true` 表示命令已交给 Toolkit 执行，**不等于硬件已确认**——请稍后重新 `GET /` 对比读数
- Toolkit 未运行时端口不监听；插件目录运行期字节不变（日志写在 `%LOCALAPPDATA%\TbtHttpBridge\`）

### 包内容与校验

| 文件 | 说明 |
|---|---|
| `plugin.json` | 插件清单（Id `tbts.http-bridge`，API v1） |
| `TbtHttpBridge.dll` | 插件本体，SHA256 `C340A6ADB3FB4B663C2234F2846F70961AA6CD2DC537DA70EDC8F0667CD9F120` |
| `TbtHttpBridge.deps.json` | 零依赖声明（不含契约程序集） |

> 包内 DLL 的哈希可复现（`Deterministic` 构建）；zip 哈希不可复现（压缩会写入时间戳），
> 所以校验请以包内 DLL 的 SHA256 为准。完整清单见附件 `SHA256SUMS-v1.1.0.txt`。
