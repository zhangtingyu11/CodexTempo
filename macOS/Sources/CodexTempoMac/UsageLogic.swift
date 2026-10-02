import Foundation
import CryptoKit

enum AccountContext {
    static func hash(_ value: String) -> String {
        SHA256.hash(data: Data(value.utf8)).map { String(format: "%02x", $0) }.joined()
    }
    static func stamp() -> String {
        let home = CodexPathResolver.resolveHome()
        let path = home.appendingPathComponent("auth.json").path
        let attrs = try? FileManager.default.attributesOfItem(atPath: path)
        return hash(home.path + "|" + String(describing: attrs?[.size]) + "|" + String(describing: attrs?[.modificationDate]))
    }
}

enum UsageLogic {
    static func sameWindow(_ a: LimitWindow?, _ b: LimitWindow?) -> Bool {
        guard let a, let b else { return false }
        return a.windowMinutes == b.windowMinutes && abs(a.resetsAt.timeIntervalSince(b.resetsAt)) <= 90
    }
    static func sameAccount(_ a: UsageSnapshot, _ b: UsageSnapshot) -> Bool {
        a.accountKey != nil && a.accountKey == b.accountKey && a.contextStamp == b.contextStamp
    }
    static func drops(_ current: UsageSnapshot, _ previous: UsageSnapshot) -> Bool {
        guard sameAccount(current, previous) else { return false }
        return (sameWindow(current.week, previous.week) && current.week!.usedPercent < previous.week!.usedPercent)
            || (sameWindow(current.fiveHour, previous.fiveHour) && current.fiveHour!.usedPercent < previous.fiveHour!.usedPercent)
    }
    static func trackDay(_ current: UsageSnapshot, _ previous: UsageSnapshot?) -> UsageSnapshot {
        var result = current
        let midnight = Calendar.current.startOfDay(for: current.capturedAt)
        var baseline: Double?
        if let previous, sameAccount(current, previous), sameWindow(current.week, previous.week) {
            if previous.usageDay == midnight { baseline = previous.dayStartUsed }
            else if previous.capturedAt <= midnight && midnight.timeIntervalSince(previous.capturedAt) <= 1_200 {
                baseline = previous.week?.usedPercent
            }
        }
        if let value = baseline, value > (current.week?.usedPercent ?? -1) { baseline = nil }
        result.usageDay = midnight
        result.dayStartUsed = baseline
        result.todayUsedPercent = baseline.flatMap { base in current.week.map { $0.usedPercent - base } }
        return result
    }
    static func active(_ snapshot: UsageSnapshot?, now: Date) -> UsageSnapshot? {
        guard var result = snapshot else { return nil }
        if (result.fiveHour?.resetsAt ?? .distantPast) <= now { result.fiveHour = nil }
        if (result.week?.resetsAt ?? .distantPast) <= now { result.week = nil }
        guard result.week != nil || result.fiveHour != nil else { return nil }
        if !Calendar.current.isDate(result.capturedAt, inSameDayAs: now) { result.todayUsedPercent = nil }
        result.source = CodexUsageProvider.cachedSourceName
        return result
    }
}
