# 更新日志 / Changelog

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
