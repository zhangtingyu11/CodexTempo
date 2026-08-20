import AppKit

@MainActor
protocol MenuBarWindowConfigurable: AnyObject {
    var hidesOnDeactivate: Bool { get set }
    var sharingType: NSWindow.SharingType { get set }
}

extension NSWindow: MenuBarWindowConfigurable {}

enum MenuBarWindowPolicy {
    @MainActor
    static func configure(_ window: MenuBarWindowConfigurable) {
        window.hidesOnDeactivate = false
        window.sharingType = .readOnly
    }
}
