import AppKit
import Dispatch
import SwiftUI

final class TempoAppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        if CommandLine.arguments.contains("--dark-preview") {
            NSApp.appearance = NSAppearance(named: .darkAqua)
        }
        NSApp.setActivationPolicy(.accessory)
        if let iconURL = Bundle.main.url(forResource: "CodexTempo", withExtension: "icns"),
           let icon = NSImage(contentsOf: iconURL) {
            NSApp.applicationIconImage = icon
        } else {
            NSApp.applicationIconImage = NSImage(systemSymbolName: "leaf", accessibilityDescription: "Codex Tempo")
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }
}

struct CodexTempoApp: App {
    @NSApplicationDelegateAdaptor(TempoAppDelegate.self) private var delegate
    @StateObject private var model = AppModel()
    private let previewColorScheme: ColorScheme? = CommandLine.arguments.contains("--dark-preview") ? .dark : nil

    var body: some Scene {
        Window("Codex Tempo", id: "tempo") {
            TempoPanelView(model: model)
                .background(WindowConfigurator(model: model))
                .preferredColorScheme(previewColorScheme)
        }
        .windowStyle(.hiddenTitleBar)
        .windowResizability(.contentSize)
        .defaultPosition(.bottomTrailing)

        MenuBarExtra {
            MenuPopoverView(model: model)
                .preferredColorScheme(previewColorScheme)
        } label: {
            HStack(spacing: 3) {
                Image(systemName: "leaf")
                if let remaining = model.snapshot?.week?.remainingPercent {
                    Text("\(Int(remaining.rounded()))%")
                }
            }
        }
        .menuBarExtraStyle(.window)
    }
}

@main
enum CodexTempoEntry {
    static func main() {
        if CommandLine.arguments.contains("--self-test") {
            Task {
                let passed = await SelfTests.run()
                exit(passed ? 0 : 1)
            }
            dispatchMain()
        } else if CommandLine.arguments.contains("--dump-usage") {
            Task {
                let snapshot = await CodexUsageProvider().readLatest()
                if let snapshot {
                    print(Self.json(snapshot))
                    exit(0)
                }
                print("No Codex usage snapshot found")
                exit(1)
            }
            dispatchMain()
        } else {
            if CommandLine.arguments.contains("--dark-preview") {
                NSApplication.shared.appearance = NSAppearance(named: .darkAqua)
            }
            CodexTempoApp.main()
        }
    }

    private static func json(_ snapshot: UsageSnapshot) -> String {
        func window(_ value: LimitWindow?) -> Any {
            guard let value else { return NSNull() }
            return [
                "usedPercent": value.usedPercent,
                "remainingPercent": value.remainingPercent,
                "windowMinutes": value.windowMinutes,
                "resetsAt": ISO8601DateFormatter().string(from: value.resetsAt)
            ]
        }
        let object: [String: Any] = [
            "fiveHour": window(snapshot.fiveHour),
            "week": window(snapshot.week),
            "capturedAt": ISO8601DateFormatter().string(from: snapshot.capturedAt),
            "source": snapshot.source,
            "todayUsedPercent": snapshot.todayUsedPercent ?? NSNull()
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys]) else {
            return "{}"
        }
        return String(data: data, encoding: .utf8) ?? "{}"
    }
}
