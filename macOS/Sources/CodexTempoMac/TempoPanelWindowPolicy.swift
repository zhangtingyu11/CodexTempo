import AppKit

@MainActor
protocol TempoPanelWindowLevelConfigurable: AnyObject {
    var level: NSWindow.Level { get set }
}

extension NSWindow: TempoPanelWindowLevelConfigurable {}

enum TempoPanelWindowPolicy {
    @MainActor
    static func applyLevel(isPinned: Bool, to window: TempoPanelWindowLevelConfigurable) {
        window.level = isPinned ? .floating : .normal
    }
}
