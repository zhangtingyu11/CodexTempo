## v1.0.9

本次版本修复剩余额度在两个数值之间来回跳变的问题。

- 原因是 App Server 偶发一次失败时，旧版会立即切回较旧的 session 快照。
- 现在已获得官方值后，短暂失败会保留最后可信值。
- 同一重置周期内使用量只能向上，拦截过期节点返回的较低数值。
- 真正进入新的重置周期时，仍会正常接受较低初始值。

建议所有 v1.0.8 用户升级。

---

This release fixes remaining quota values jumping back and forth.

- A brief App Server failure previously caused an immediate switch to an older session snapshot.
- The widget now retains the last trusted official value during transient failures.
- Usage is monotonic within the same reset window, filtering stale replica responses.
- A genuine new reset window still accepts its lower initial value normally.

Recommended for all v1.0.8 users.
