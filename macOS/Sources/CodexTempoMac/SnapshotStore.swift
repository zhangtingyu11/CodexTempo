import Foundation

struct SnapshotStore {
    private static let key = "codexTempo.lastSnapshot"
    private let defaults: UserDefaults
    private let now: () -> Date

    init(
        defaults: UserDefaults = .standard,
        now: @escaping () -> Date = Date.init
    ) {
        self.defaults = defaults
        self.now = now
    }

    func load() -> UsageSnapshot? {
        guard let data = defaults.data(forKey: Self.key) else { return nil }
        guard let snapshot = try? JSONDecoder().decode(UsageSnapshot.self, from: data) else {
            defaults.removeObject(forKey: Self.key)
            return nil
        }
        let current = now()
        guard snapshot.fiveHour?.resetsAt ?? .distantPast > current ||
                snapshot.week?.resetsAt ?? .distantPast > current else {
            defaults.removeObject(forKey: Self.key)
            return nil
        }
        return snapshot
    }

    func save(_ snapshot: UsageSnapshot) {
        guard let data = try? JSONEncoder().encode(snapshot) else { return }
        defaults.set(data, forKey: Self.key)
    }

    func clear() {
        defaults.removeObject(forKey: Self.key)
    }
}
