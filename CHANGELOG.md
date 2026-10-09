# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 与 [语义化版本](https://semver.org/lang/zh-CN/)。
插件自身的版本（`plugin.json` 的 `Version`）与宿主 ThinkBook Toolkit 的版本相互独立。

## [1.1.0] - 2026-10-09

### Added

- 插件页改用**原生设置分组卡片**：标题 + 说明 + 图标（UI 与会话主题/语言一致）。
- 每项设置补充双语说明与图标（`Description` / `Glyph`）。
- 作者署名：咕咕咕---TB大王。

### Changed

- **联动模式选项改为中文**（`关闭` / `仅共享数据` / `允许共享数据和调整部分设置`），与旧版 Toolkit 界面用词一致；
  插件内部仍兼容升级前写入的 ASCII 值（`share-data-only` 等），不重选也能继续工作。
- 插件名简化为「本机 HTTP 联动」，原括号说明移入设置卡片。

### Notes

- HTTP 契约、权限、打包结构（仅 3 个文件）均未变化，从 1.0.0 升级只需「卸载 → 导入 → 重新审核」。
- 本版本 Release 资产 DLL SHA256：`5760BC8EB4AD4974E90F6C2082845958178E6084DDEE7BC986B179D80436D506`
  （该资产构建于加入 PathMap 之前；从本仓库源码构建会得到 `C340A6ADB3FB4B663C2234F2846F70961AA6CD2DC537DA70EDC8F0667CD9F120`，功能相同）

## [1.0.0] - 2026-10-09

### Added

- 首个版本：以插件形式恢复 ThinkBook Toolkit 1.1.0 移除的「与其它软件联动」本机 HTTP 接口
  （移植自 TBT v1.0.4 的 `LocalDataSharingService.cs`，MPL-2.0）。
- `GET /` 传感器快照（7 字段，camelCase，与旧版逐字一致）。
- `POST /performance-mode`、`POST /fan-strategy`、`POST /fan-full-speed` 控制路由。
- `GET /status` 只读状态端点（新增，排障用）。
- 离线宿主夹具 `tests/HostSim`（39 项断言）。
