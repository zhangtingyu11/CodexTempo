## v1.0.8

本次版本从根源上修复额度时间停住的问题。

- 主数据源改为 Codex 官方 App Server，不再等待 session 写入额度快照。
- 每 10 秒复用同一个本地连接查询实时额度。
- 官方接口不可用时自动回退本地 session，不影响基本显示。
- 状态栏显示“实时查询”与本次官方查询时间。

建议所有 v1.0.7 用户升级。

---

This release fixes allowance timestamps getting stuck at the source.

- The primary data source is now the official Codex App Server rather than session snapshots.
- One local connection is reused for live queries every 10 seconds.
- Local session data remains available as an automatic fallback.
- The status clearly shows the latest live query time.

Recommended for all v1.0.7 users.
