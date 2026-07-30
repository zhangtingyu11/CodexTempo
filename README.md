# Codex Tempo

一个轻量、无网络请求的 Windows Codex 额度桌面小组件。项目不包含个人路径或账号信息，
可以直接 fork、构建和分发。

Codex Tempo 每 10 秒读取一次本机最新的额度快照，用 5 小时窗口保护短期用量，
用一周窗口规划长期节奏。界面显示的百分比均为**剩余额度**。

## 节奏建议是什么意思？

周节奏以“剩余额度 ÷ 距离重置的时间”计算：

- `1.0× 周均速`：从现在开始均匀使用，刚好在本周额度重置前用完。
- `1.4× 周均速`：建议速度约为均匀速度的 1.4 倍，当前可以多用一些。
- `0.7× 周均速`：建议降到均匀速度的约 70%，避免太早耗尽。
- 5 小时额度较低时，会自动压低建议，避免为了追周进度而提前撞限额。

这里的倍数是**额度消耗速度**，不是精确的消息条数。不同模型、上下文长度和任务复杂度
消耗不同，因此小组件不会虚构“每小时可发多少条”。

建议行同时显示：

- 今天估计已经使用的额度。
- 今天的目标额度以及还可以使用多少。
- 本周累计已用额度。

今日已用量优先使用昨天最后一条额度快照与当前快照的差值估算；没有昨天快照时，
使用今天第一条快照作为基线。它不是官方逐日账单。

## 功能

- 10 秒自动刷新与文件变更监听。
- 窗口任意非按钮区域均可拖动。
- 可切换置顶，按钮颜色和图标会显示当前状态。
- 碰到当前显示器边缘即自动吸附：左右显示竖向额度条，上下显示横向额度条。
- `—` 按钮收起到最近的屏幕边缘；双击紧凑额度条或将其拖离边缘即可恢复。
- 同时保留任务栏与托盘入口，避免窗口无法找回。
- `×` 按钮彻底退出，不留下隐藏进程；再次启动会创建全新窗口。
- 不限制单实例：每次双击 EXE 都会明确打开一个新窗口，不会静默退出。
- 无网络请求，不读取 `auth.json`。
- 过期的 5 小时或周窗口不会作为当前数据展示。
- 支持 x64 与 ARM64 Windows。

## 本地运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

```powershell
dotnet run --project .\CodexTempo\CodexTempo.csproj
```

## 构建单文件版

框架依赖版：

```powershell
dotnet publish .\CodexTempo\CodexTempo.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -o .\release
```

无需预装 .NET 的独立版：

```powershell
dotnet publish .\CodexTempo\CodexTempo.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o .\release
```

## 数据位置

默认读取：

```text
%USERPROFILE%\.codex\sessions
```

如设置了 `CODEX_HOME`，则读取 `%CODEX_HOME%\sessions`，适合自定义安装路径。

## GitHub 发布

仓库内置 GitHub Actions。推送 `v*` 标签后会自动构建并发布：

- `CodexTempo-win-x64.zip`
- `CodexTempo-win-arm64.zip`

两个版本均为 self-contained，使用者无需另外安装 .NET。

下载对应架构的 ZIP，解压后运行 `CodexTempo.exe` 即可。程序目前未进行商业代码签名，
Windows SmartScreen 可能在首次启动时显示“未知发布者”提示。

## 资源与隐私

- WPF 原生窗口，不嵌入 Chromium。
- 文件监听器跟踪最新 session；文件未变化时直接使用内存缓存，变化后只读取增量数据。
- 关闭窗口即退出，不留下后台服务。
- 只读扫描 session JSONL 中的额度快照；不读取 `auth.json`，不访问网络，也不上传数据。
- 不包含硬编码用户名、个人目录、账号或令牌；每位用户运行时动态解析自己的 `%USERPROFILE%`
  或 `CODEX_HOME`。

## License

[MIT](LICENSE)
