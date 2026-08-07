import AppKit
import SwiftUI

private enum ApplePalette {
    static let accent = Color(red: 0, green: 122 / 255, blue: 1)
    static let success = Color(red: 52 / 255, green: 199 / 255, blue: 89 / 255)
    static let warning = Color(red: 1, green: 159 / 255, blue: 10 / 255)
    static let error = Color(red: 1, green: 59 / 255, blue: 48 / 255)

    static func background(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(red: 0.11, green: 0.11, blue: 0.12) : hex(0xF5F5F7)
    }

    static func card(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(red: 0.17, green: 0.17, blue: 0.18) : .white
    }

    static func primary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xF5F5F7) : hex(0x1D1D1F)
    }

    static func secondary(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(red: 0.64, green: 0.64, blue: 0.67) : hex(0x6E6E73)
    }

    static func divider(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(red: 0.25, green: 0.25, blue: 0.27) : hex(0xD2D2D7)
    }

    static func border(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(red: 0.25, green: 0.25, blue: 0.27) : hex(0xE5E5EA)
    }

    static func tagBackground(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(red: 0.22, green: 0.22, blue: 0.24) : hex(0xF2F2F7)
    }

    static func tagText(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? hex(0xF2F2F7) : hex(0x3A3A3C)
    }

    private static func hex(_ value: Int) -> Color {
        Color(
            red: Double((value >> 16) & 0xFF) / 255,
            green: Double((value >> 8) & 0xFF) / 255,
            blue: Double(value & 0xFF) / 255
        )
    }
}

struct TempoPanelView: View {
    @ObservedObject var model: AppModel
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            identityBar

            Spacer().frame(height: 18)

            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Text(model.advice.title)
                    .font(.system(size: 30, weight: .semibold))
                    .tracking(-0.45)
                    .lineLimit(1)

                Spacer(minLength: 8)

                Text(model.advice.rateLabel)
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(ApplePalette.tagText(colorScheme))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 5)
                    .background(ApplePalette.tagBackground(colorScheme), in: Capsule())
            }

            Text(model.advice.detail)
                .font(.system(size: 13, weight: .regular))
                .foregroundStyle(ApplePalette.secondary(colorScheme))
                .lineLimit(1)
                .minimumScaleFactor(0.82)
                .padding(.top, 6)

            Spacer().frame(height: 20)

            HStack(spacing: 14) {
                QuotaCard(title: "5 小时额度", window: model.snapshot?.fiveHour)
                QuotaCard(title: "每周额度", window: model.snapshot?.week)
            }

            Spacer().frame(height: 18)

            footer
        }
        .padding(.horizontal, 24)
        .padding(.top, 20)
        .padding(.bottom, 18)
        .frame(width: 420, height: 340, alignment: .topLeading)
        .background(ApplePalette.background(colorScheme))
        .foregroundStyle(ApplePalette.primary(colorScheme))
    }

    private var identityBar: some View {
        HStack(spacing: 8) {
            TempoAppIcon(size: 24)

            Text("Codex Tempo")
                .font(.system(size: 13, weight: .semibold))

            Spacer()

            chromeButton(
                symbol: model.isPinned ? "pin" : "pin.slash",
                help: model.isPinned ? "取消置顶" : "保持在最前"
            ) {
                model.isPinned.toggle()
            }
            chromeButton(symbol: "minus", help: "隐藏面板") {
                model.hideWindow()
            }
        }
        .buttonStyle(.plain)
    }

    private var footer: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(statusColor)
                .frame(width: 6, height: 6)
            Text(model.syncLabel)
                .font(.system(size: 11, weight: .regular))
                .foregroundStyle(ApplePalette.secondary(colorScheme))

            Spacer()

            Button {
                model.refresh()
            } label: {
                HStack(spacing: 6) {
                    if model.isRefreshing {
                        ProgressView().controlSize(.small)
                    } else {
                        Image(systemName: "arrow.clockwise")
                    }
                    Text("刷新")
                }
                .font(.system(size: 12, weight: .medium))
            }
            .buttonStyle(.plain)
            .foregroundStyle(ApplePalette.accent)
            .help("每 10 秒自动刷新")
        }
    }

    private var statusColor: Color {
        if model.isShowingStartupCache { return ApplePalette.warning }
        if model.snapshot?.source == CodexAppServerClient.sourceName { return ApplePalette.success }
        if model.snapshot != nil { return ApplePalette.warning }
        return ApplePalette.error
    }

    private func chromeButton(symbol: String, help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 13, weight: .regular))
                .frame(width: 26, height: 26)
                .contentShape(Rectangle())
        }
        .foregroundStyle(ApplePalette.secondary(colorScheme))
        .help(help)
    }
}

private struct TempoAppIcon: View {
    let size: CGFloat

    var body: some View {
        Group {
            if let url = Bundle.main.url(forResource: "CodexTempo", withExtension: "icns"),
               let image = NSImage(contentsOf: url) {
                Image(nsImage: image)
                    .resizable()
                    .interpolation(.high)
            } else {
                Image(systemName: "leaf")
                    .resizable()
                    .scaledToFit()
                    .foregroundStyle(ApplePalette.accent)
                    .padding(4)
                    .background(ApplePalette.accent.opacity(0.10), in: RoundedRectangle(cornerRadius: 7))
            }
        }
        .scaledToFit()
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}

private struct QuotaCard: View {
    let title: String
    let window: LimitWindow?
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title)
                .font(.system(size: 13, weight: .regular))
                .foregroundStyle(ApplePalette.secondary(colorScheme))

            Text(percentage)
                .font(.system(size: 38, weight: .bold, design: .rounded))
                .tracking(-0.8)
                .foregroundStyle(ApplePalette.accent)
                .padding(.top, 6)

            Spacer(minLength: 8)

            AppleProgressBar(value: window?.remainingPercent ?? 0)

            Text(resetText)
                .font(.system(size: 12, weight: .regular))
                .foregroundStyle(ApplePalette.secondary(colorScheme))
                .padding(.top, 8)
        }
        .padding(16)
        .frame(maxWidth: .infinity, minHeight: 136, alignment: .topLeading)
        .background(ApplePalette.card(colorScheme))
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(ApplePalette.border(colorScheme), lineWidth: 0.75)
        }
    }

    private var percentage: String {
        window.map { "\(Int($0.remainingPercent.rounded()))%" } ?? "—"
    }

    private var resetText: String {
        guard let window else { return "等待额度数据" }
        return "\(RecommendationEngine.formatDuration(window.timeRemaining(at: Date()))) 后重置"
    }
}

private struct AppleProgressBar: View {
    let value: Double
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule().fill(ApplePalette.border(colorScheme))
                Capsule()
                    .fill(ApplePalette.accent)
                    .frame(width: geometry.size.width * min(max(value, 0), 100) / 100)
            }
        }
        .frame(height: 5)
        .animation(.easeOut(duration: 0.25), value: value)
        .accessibilityLabel("剩余额度")
        .accessibilityValue("\(Int(value.rounded()))%")
    }
}

struct MenuPopoverView: View {
    @ObservedObject var model: AppModel
    @Environment(\.colorScheme) private var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Label("Codex Tempo", systemImage: "leaf")
                    .font(.system(size: 15, weight: .semibold))
                Spacer()
                Text(model.advice.rateLabel)
                    .font(.system(size: 11, weight: .medium))
                    .foregroundStyle(ApplePalette.tagText(colorScheme))
                    .padding(.horizontal, 10)
                    .padding(.vertical, 5)
                    .background(ApplePalette.tagBackground(colorScheme), in: Capsule())
            }

            VStack(alignment: .leading, spacing: 6) {
                Text(model.advice.title)
                    .font(.system(size: 22, weight: .semibold))
                Text(model.advice.detail)
                    .font(.system(size: 13))
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack(spacing: 10) {
                compactQuota("5 小时", model.snapshot?.fiveHour)
                compactQuota("本周", model.snapshot?.week)
            }

            HStack(spacing: 7) {
                Circle().fill(statusColor).frame(width: 6, height: 6)
                Text(model.syncLabel)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
            }

            Divider()

            VStack(spacing: 10) {
                Toggle("登录时启动", isOn: Binding(
                    get: { model.launchAtLoginEnabled },
                    set: { model.setLaunchAtLogin($0) }
                ))
                Toggle("窗口置顶", isOn: $model.isPinned)
            }
            .toggleStyle(.switch)
            .controlSize(.small)
            .tint(ApplePalette.accent)

            if let message = model.settingsMessage {
                Text(message)
                    .font(.system(size: 12))
                    .foregroundStyle(ApplePalette.warning)
            }

            Divider()

            HStack {
                Button("显示面板") { model.showWindow() }
                    .buttonStyle(.borderedProminent)
                    .tint(ApplePalette.accent)
                Button {
                    model.refresh()
                } label: {
                    Label("刷新", systemImage: "arrow.clockwise")
                }
                .buttonStyle(.bordered)
                Spacer()
                Button("退出") { NSApp.terminate(nil) }
                    .buttonStyle(.plain)
            }
        }
        .padding(16)
        .frame(width: 320)
    }

    private func compactQuota(_ title: String, _ window: LimitWindow?) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title)
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
            Text(window.map { "\(Int($0.remainingPercent.rounded()))%" } ?? "—")
                .font(.system(size: 28, weight: .bold, design: .rounded))
                .foregroundStyle(ApplePalette.accent)
            Text(window.map { "\(RecommendationEngine.formatDuration($0.timeRemaining(at: Date()))) 后重置" } ?? "等待数据")
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(ApplePalette.card(colorScheme))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(ApplePalette.border(colorScheme), lineWidth: 0.75)
        }
    }

    private var statusColor: Color {
        if model.isShowingStartupCache { return ApplePalette.warning }
        if model.snapshot?.source == CodexAppServerClient.sourceName { return ApplePalette.success }
        if model.snapshot != nil { return ApplePalette.warning }
        return ApplePalette.error
    }
}

struct WindowConfigurator: NSViewRepresentable {
    let model: AppModel

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async { [weak view] in
            if let window = view?.window { model.configure(window) }
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        DispatchQueue.main.async {
            if let window = nsView.window { model.configure(window) }
        }
    }
}
