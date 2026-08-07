import Foundation

enum CodexPathResolver {
    static func resolveHome(
        environment: [String: String] = ProcessInfo.processInfo.environment,
        home: URL = FileManager.default.homeDirectoryForCurrentUser
    ) -> URL {
        if let configured = environment["CODEX_HOME"], !configured.trimmingCharacters(in: .whitespaces).isEmpty {
            return URL(fileURLWithPath: configured).standardizedFileURL
        }
        return home.appendingPathComponent(".codex", isDirectory: true)
    }
}
