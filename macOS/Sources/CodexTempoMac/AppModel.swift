import AppKit
import Combine
import Foundation

@MainActor
final class AppModel: ObservableObject {
    @Published private(set) var snapshot: UsageSnapshot?
    @Published private(set) var advice: PaceAdvice
    @Published private(set) var syncLabel: String
    @Published private(set) var isRefreshing = false
    @Published private(set) var isShowingStartupCache = false
    @Published private(set) var settingsMessage: String?
    @Published private(set) var launchAtLoginEnabled: Bool
    @Published var isPinned = true {
        didSet { applyWindowLevel() }
    }

    private let provider: CodexUsageProvider
    private let snapshotStore: SnapshotStore
    private let launchAtLogin: LaunchAtLoginController
    private var pollingTask: Task<Void, Never>?

    init(
        provider: CodexUsageProvider = CodexUsageProvider(),
        snapshotStore: SnapshotStore = SnapshotStore(),
        launchAtLogin: LaunchAtLoginController = LaunchAtLoginController()
    ) {
        self.provider = provider
        self.snapshotStore = snapshotStore
        self.launchAtLogin = launchAtLogin
        launchAtLoginEnabled = launchAtLogin.isEnabled
        do {
            snapshot = nil
            advice = PaceAdvice(
                title: "正在读取额度",
                detail: "正在连接本机 Codex",
                rateLabel: "本地待命",
                rateMultiplier: 0,
                dailyBudgetPercent: 0,
                tone: .waiting
            )
            syncLabel = "正在连接…"
        }
        pollingTask = Task { [weak self] in
            await self?.poll()
        }
    }

    func refresh() {
        guard !isRefreshing else { return }
        Task { [weak self] in
            await self?.provider.requestRefresh()
            await self?.refreshNow()
        }
    }

    func showWindow() {
        NSApp.activate(ignoringOtherApps: true)
        if let window = NSApp.windows.first(where: { $0.title == "Codex Tempo" }) ?? NSApp.windows.first {
            window.makeKeyAndOrderFront(nil)
            configure(window)
        }
    }

    func hideWindow() {
        NSApp.windows.filter { $0.title == "Codex Tempo" }.forEach { $0.orderOut(nil) }
    }

    func setLaunchAtLogin(_ enabled: Bool) {
        do {
            try launchAtLogin.setEnabled(enabled)
            launchAtLoginEnabled = launchAtLogin.isEnabled
            settingsMessage = nil
        } catch {
            launchAtLoginEnabled = launchAtLogin.isEnabled
            settingsMessage = enabled
                ? "无法开启登录启动，请在系统设置中允许"
                : "无法关闭登录启动：\(error.localizedDescription)"
        }
    }

    func configure(_ window: NSWindow) {
        window.title = "Codex Tempo"
        window.level = isPinned ? .floating : .normal
        window.isMovableByWindowBackground = true
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.setFrameAutosaveName("CodexTempoCompactPremium")
        window.standardWindowButton(.zoomButton)?.isHidden = true
        window.standardWindowButton(.miniaturizeButton)?.isHidden = true
    }

    private func poll() async {
        while !Task.isCancelled {
            await refreshNow()
            try? await Task.sleep(nanoseconds: 10_000_000_000)
        }
    }

    private func refreshNow() async {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }

        guard let value = await provider.readLatest() else {
            snapshot = nil
            isShowingStartupCache = false
            advice = PaceAdvice(
                title: "暂时无法获取额度",
                detail: "请确认已登录 Codex 且网络可用，将自动重试",
                rateLabel: "本地待命",
                rateMultiplier: 0,
                dailyBudgetPercent: 0,
                tone: .waiting
            )
            syncLabel = "未找到 Codex 额度数据"
            return
        }

        snapshot = value
        isShowingStartupCache = value.source != CodexAppServerClient.sourceName
        advice = RecommendationEngine.recommend(snapshot: value, now: Date())
        let formatter = DateFormatter()
        formatter.dateFormat = "MM-dd HH:mm:ss"
        let time = formatter.string(from: value.capturedAt)
        if value.source == CodexAppServerClient.sourceName {
            syncLabel = "实时查询 · \(time)"
        } else if value.source == CodexUsageProvider.cachedSourceName {
            syncLabel = "连接波动 · 保留 \(time)"
        } else {
            syncLabel = "本地快照 · \(time)"
        }
    }

    private func applyWindowLevel() {
        NSApp.windows.forEach { $0.level = isPinned ? .floating : .normal }
    }

    func quit() {
        pollingTask?.cancel()
        Task { await provider.shutdown(); NSApp.terminate(nil) }
    }
}
