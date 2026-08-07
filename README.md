# Codex Tempo

一个轻量、安静的 Windows 与 macOS Codex 额度桌面小组件。

它每 10 秒通过本机 Codex App Server 查询一次当前额度，同时展示 5 小时与每周剩余额度，并根据本周进度告诉你今天用到多少比较合适。

![Codex Tempo 界面](assets/app-preview.png)

## 下载

[下载最新版 Windows 安装包](https://github.com/zhangtingyu11/CodexTempo/releases/latest/download/CodexTempo-Setup-x64.exe)

双击安装即可，无需配置，也无需额外安装 .NET。程序未进行商业代码签名，Windows 首次运行时可能显示“未知发布者”。

[下载最新版 macOS 通用安装镜像](https://github.com/zhangtingyu11/CodexTempo/releases/latest/download/CodexTempo-macOS-universal.dmg)

打开 DMG 后把 Codex Tempo 拖入“应用程序”。安装包同时支持 Apple 芯片和 Intel Mac。由于目前没有 Apple Developer ID，首次打开时可能需要在 Finder 中右键应用并选择“打开”。

### macOS 源码构建

macOS 13 或更高版本可使用 Apple Command Line Tools 构建（若未安装，先运行 `xcode-select --install`）：

```bash
cd macOS
./scripts/build-app.sh
open build/CodexTempo.app
```

应用会显示原生浮动面板，并常驻菜单栏。它优先通过本机 `codex app-server` 查询实时额度；请确保已安装 Codex CLI，或已使用 Codex 产生过 `~/.codex/sessions` 本地快照。若 Codex CLI 安装在非标准位置，可在启动前设置 `CODEX_EXECUTABLE=/完整路径/codex`。

macOS 版还支持菜单栏直接显示每周剩余额度、启动时立即恢复上次快照、记忆窗口位置、窗口置顶、登录时启动，以及跟随系统的浅色/深色外观。

Windows 与 macOS 使用统一的 Apple 灰、系统蓝、额度卡片和蓝色叶片图标；Windows 版会跟随系统深浅色，并记住上次窗口位置。

## 功能

- 5 小时与每周额度实时显示
- 今日建议用量与使用节奏提醒
- 每 10 秒通过 Codex 官方本地接口查询实时额度
- Windows 触碰屏幕边缘自动缩成紧凑模式
- Windows 提供托盘入口、桌面快捷方式和关闭选择；macOS 常驻菜单栏并显示周额度
- 两个平台均支持窗口置顶和登录时启动
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

[Download the latest universal macOS disk image](https://github.com/zhangtingyu11/CodexTempo/releases/latest/download/CodexTempo-macOS-universal.dmg)

Open the DMG and drag Codex Tempo to Applications. The app supports both Apple silicon and Intel Macs. Because it does not yet have an Apple Developer ID signature, first launch may require right-clicking the app in Finder and choosing Open.

### macOS source build

On macOS 13 or newer (install Apple Command Line Tools first with `xcode-select --install`):

```bash
cd macOS
./scripts/build-app.sh
open build/CodexTempo.app
```

The native floating panel also lives in the menu bar. It prefers the local `codex app-server` live API and falls back to `~/.codex/sessions`. Set `CODEX_EXECUTABLE=/full/path/to/codex` before launch if the CLI is installed in a non-standard location.

The macOS build also shows weekly allowance directly in the menu bar, restores the last snapshot immediately, remembers window position, supports always-on-top and launch at login, and follows the system light/dark appearance.

Windows and macOS share the same Apple gray and system-blue visual language, quota cards, and blue leaf icon. The Windows build follows the system light/dark theme and remembers its last window position.

### Features

- Live 5-hour and weekly allowance
- Suggested daily usage and pacing guidance
- Live allowance queries through the official local Codex interface every 10 seconds
- Edge-triggered compact mode on Windows
- Windows tray access, desktop shortcut, and close choice; native menu-bar status on macOS
- Always-on-top and optional launch at login on both platforms
- Automatic local-session fallback when the official interface is unavailable
- Keeps the last trusted value during brief connection failures to prevent quota jumps
- The widget does not parse conversation content or upload data to a developer-operated server

MIT License
