## v1.0.7

本次版本修复了多任务并行使用 Codex 时额度可能不刷新的问题。

- 扩大并发 session 的检查范围，避免通用额度快照被其他任务挤出。
- 状态栏现在显示真实额度快照时间，不再用轮询时间冒充更新时间。
- 增加多 session 与追加写入的自动回归测试。

建议所有 v1.0.6 用户升级。

---

This release fixes stale allowance values when multiple Codex tasks are active.

- Broader concurrent-session coverage prevents the general allowance snapshot from being crowded out.
- The status now shows the actual snapshot time rather than the polling time.
- Added automated regression tests for concurrent sessions and appended snapshots.

Recommended for all v1.0.6 users.
