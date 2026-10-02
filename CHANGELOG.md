# 更新日志 / Changelog

## v1.1.2（待发布 / Unreleased）

- 两端按本地自然日分配剩余预算，未知今日量不再显示为零。
- 正常刷新仅使用官方额度；每日基线按账号隔离并持久化，不再扫描聊天记录估算。
- 低值连续确认后可纠正，缓存保留原时间；独立清除过期窗口并在失败时退避重试。
- Windows 单实例唤回、紧凑失联状态点、多屏位置检查及吸附模式动态主题修复。
- 两端诊断读取加入有界数据、无结果缓存及缓存数量上限；新增边界回归测试。
- Use calendar-day budgets, account-scoped official observations, and explicit unknown daily usage.
- Preserve cache timestamps, confirm lower readings, expire windows separately, and back off failed queries.
- Add Windows single-instance activation and compact stale-state indicators; fix theme and monitor restoration.
- Bound diagnostic transcript reads and caches, and expand regression tests.

## v1.1.1

- Windows 保存最后一次官方额度，重启后先显示带时间的缓存，再自动实时查询；已过期额度不继续显示。
- Windows 可在桌面应用未打开时查找本机 Codex 后台程序。
- 右键小组件或托盘图标，可切换“开机启动”或立即刷新。
- Windows now persists the last official quota across restarts, labels cached readings, and drops expired windows.
- Discover the locally installed Codex backend without requiring the desktop app to be running.
- Right-click the widget or tray icon to toggle launch at login or refresh immediately.

## v1.1.0

- 新增 macOS 通用 DMG，支持 Apple 芯片与 Intel Mac。
- Windows 与 macOS 统一视觉和应用图标。
- Windows 新增系统深浅色适配与窗口位置记忆。
- 统一两端版本号，并优化 macOS 今日用量读取性能。

---

## v1.1.0 (English)

- Added a universal macOS DMG for Apple silicon and Intel Macs.
- Unified the visual system and application icon across platforms.
- Added system light/dark mode and remembered window placement on Windows.
- Unified versioning and optimized macOS daily-usage reads.

## v1.0.9

- 修复 App Server 短暂失败时切回旧 session，导致剩余额度来回跳变的问题。
- 已取得官方值后，连接波动只保留最后可信值。
- 同一重置周期内使用量单调防抖；新周期仍正常重置。

## v1.0.8

- 改用 Codex 官方 App Server 实时查询额度，不再依赖 session 是否写入新快照。
- 每 10 秒复用同一个本地连接；官方接口不可用时自动回退 session。
- 状态栏以“实时查询”明确显示官方接口的查询时间。

## v1.0.7

- 修复同时运行多个 Codex 任务时，通用额度快照可能被忽略、界面停留在旧数值的问题。
- “已同步”改为显示真实额度快照时间，避免把轮询时间误认为额度更新时间。
- 增加多 session 并发与文件追加刷新回归测试。

## v1.0.6

- 区分通用 Codex 额度与模型专属额度，避免模型专属空额度被误显示为本周 100%。

---

## v1.0.9 (English)

- Fixed quota jumps caused by switching to an older session snapshot after a brief App Server failure.
- Keeps the last trusted official value during transient connection errors.
- Adds monotonic smoothing within one reset window while still accepting real resets.

## v1.0.8 (English)

- Switched to the official Codex App Server for live allowance queries instead of waiting for session snapshots.
- Reuses one local connection every 10 seconds and falls back to session data when unavailable.
- The status now clearly labels the live query time.

## v1.0.7 (English)

- Fixed stale allowance values when several Codex tasks are active at the same time.
- The status now shows the actual allowance snapshot time instead of the polling time.
- Added regression coverage for concurrent sessions and appended snapshots.

## v1.0.6 (English)

- Separated the general Codex allowance from model-specific buckets to prevent a model bucket from appearing as 100% weekly remaining.
