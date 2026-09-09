## v1.1.1

本次主要修复 Windows 开机后的额度恢复，并增加启动设置。

- 保存最后一次官方额度，重启后先显示带时间的“上次记录”，再自动查询最新额度。
- 改进本机 Codex 后台程序查找，支持桌面应用未打开时查询；需要已登录 Codex 且网络可用。
- 右键小组件或托盘图标，可勾选/取消“开机启动”，也可点击“立即刷新”。
- 过期额度不再继续显示，跨天后不沿用昨天的今日用量。
- macOS 本次无功能变更，安装包版本同步为 1.1.1。

Windows 用户下载 **CodexTempo-Setup-x64.exe** 安装即可。旧版本仍保留在 Releases 中。

---

This release improves Windows startup quota recovery and adds launch-at-login controls.

- Restore the last official quota after a restart, clearly label its timestamp, then query fresh limits automatically.
- Improve local Codex backend discovery so queries can run without opening the desktop app. An existing Codex login and network access are required.
- Right-click the widget or tray icon to toggle launch at login or refresh immediately.
- Drop expired quota windows and clear yesterday's daily-usage estimate after midnight.
- No functional macOS changes; its package version is aligned to 1.1.1.

On Windows, download and install **CodexTempo-Setup-x64.exe**. Previous releases remain available.
