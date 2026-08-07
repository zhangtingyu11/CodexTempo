# Codex Tempo

一个轻量、安静的 Windows 与 macOS Codex 额度桌面小组件。

它每 10 秒通过本机 Codex App Server 查询一次当前额度，同时展示 5 小时与每周剩余额度，并根据本周进度告诉你今天用到多少比较合适。

![Codex Tempo 界面](assets/app-preview.png)

## 下载

[下载最新版 Windows 安装包](https://github.com/zhangtingyu11/CodexTempo/releases/latest/download/CodexTempo-Setup-x64.exe)

双击安装即可，无需配置，也无需额外安装 .NET。程序未进行商业代码签名，Windows 首次运行时可能显示“未知发布者”。

### macOS 版（源码构建）

macOS 13 或更高版本可使用 Apple Command Line Tools 构建（若未安装，先运行 `xcode-select --install`）：

```bash
cd macOS
./scripts/build-app.sh
open build/CodexTempo.app
```

应用会显示原生浮动面板，并常驻菜单栏。它优先通过本机 `codex app-server` 查询实时额度；请确保已安装 Codex CLI，或已使用 Codex 产生过 `~/.codex/sessions` 本地快照。若 Codex CLI 安装在非标准位置，可在启动前设置 `CODEX_EXECUTABLE=/完整路径/codex`。

macOS 版还支持菜单栏直接显示每周剩余额度、启动时立即恢复上次快照、记忆窗口位置、窗口置顶、登录时启动，以及跟随系统的浅色/深色外观。

v1.2.0 重新按照 Apple Human Interface 风格设计界面：使用 Apple 系统灰与蓝色、无阴影圆角卡片、以大号额度数字为核心的信息层级、统一的 SF Symbols，以及更自然积极的节奏提示。

v1.2.1 将主面板收敛为 420×340 的紧凑尺寸，进一步精修字距、边框和控件密度，并加入蓝色叶片与节奏环组合的原生 macOS 应用图标。

v1.2.2 按 macOS 图标安全区规范将图标主体缩至画布约 84%，修复它在桌面和 Finder 中比其他应用图标显得更大的问题。

## 功能

- 5 小时与每周额度实时显示
- 今日建议用量与使用节奏提醒
- 每 10 秒通过 Codex 官方本地接口查询实时额度
- 触碰屏幕边缘自动缩成紧凑模式
- 窗口置顶、托盘入口、桌面快捷方式与开机启动选项，不占用任务栏
- 关闭时可选择隐藏到托盘或彻底退出
- 官方接口不可用时自动回退本地 session
- 短暂连接波动时保留最后可信值，避免额度来回跳变
- 小组件不解析对话正文，也不向开发者服务器上传数据

## 支持

如果 Codex Tempo 对你有帮助，可以请我喝杯咖啡 ☕

<img src="assets/alipay-support.jpg" width="280" alt="支付宝支持二维码">

---

## English

Codex Tempo is a lightweight Windows and macOS desktop widget for keeping an eye on your Codex usage limits.

It queries the local Codex App Server every 10 seconds, shows the remaining 5-hour and weekly allowance, and suggests a comfortable daily pace so the weekly allowance lasts until reset.

### Download

[Download the latest Windows installer](https://github.com/zhangtingyu11/CodexTempo/releases/latest/download/CodexTempo-Setup-x64.exe)

Just run the installer—no configuration or separate .NET installation is required. The app is not commercially code-signed yet, so Windows may show an “Unknown publisher” warning on first launch.

### macOS (build from source)

On macOS 13 or newer (install Apple Command Line Tools first with `xcode-select --install`):

```bash
cd macOS
./scripts/build-app.sh
open build/CodexTempo.app
```

The native floating panel also lives in the menu bar. It prefers the local `codex app-server` live API and falls back to `~/.codex/sessions`. Set `CODEX_EXECUTABLE=/full/path/to/codex` before launch if the CLI is installed in a non-standard location.

The macOS build also shows weekly allowance directly in the menu bar, restores the last snapshot immediately, remembers window position, supports always-on-top and launch at login, and follows the system light/dark appearance.

Version 1.2.0 redesigns the dashboard around Apple Human Interface conventions: system grays and blue, shadow-free cards, large quota-first typography, consistent SF Symbols, and calmer positive pacing language.

Version 1.2.1 reduces the main panel to a compact 420×340 footprint, further refines typography and control density, and adds a native macOS icon combining a blue leaf with a tempo ring.

Version 1.2.2 scales the icon artwork to roughly 84% of its canvas, matching standard macOS visual bounds in Finder and on the desktop.

### Features

- Live 5-hour and weekly allowance
- Suggested daily usage and pacing guidance
- Live allowance queries through the official local Codex interface every 10 seconds
- Compact mode when touching a screen edge
- Always-on-top, system tray access, desktop shortcut, and optional startup without occupying the taskbar
- Choose between hiding to the tray and exiting when closing
- Automatic local-session fallback when the official interface is unavailable
- Keeps the last trusted value during brief connection failures to prevent quota jumps
- The widget does not parse conversation content or upload data to a developer-operated server

MIT License
