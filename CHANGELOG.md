# 更新日志 / Changelog

## v1.0.7

- 修复同时运行多个 Codex 任务时，通用额度快照可能被忽略、界面停留在旧数值的问题。
- “已同步”改为显示真实额度快照时间，避免把轮询时间误认为额度更新时间。
- 增加多 session 并发与文件追加刷新回归测试。

## v1.0.6

- 区分通用 Codex 额度与模型专属额度，避免模型专属空额度被误显示为本周 100%。

---

## v1.0.7 (English)

- Fixed stale allowance values when several Codex tasks are active at the same time.
- The status now shows the actual allowance snapshot time instead of the polling time.
- Added regression coverage for concurrent sessions and appended snapshots.

## v1.0.6 (English)

- Separated the general Codex allowance from model-specific buckets to prevent a model bucket from appearing as 100% weekly remaining.
