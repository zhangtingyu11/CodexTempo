import Foundation

actor CodexAppServerClient {
    static let sourceName = "Codex App Server"

    private var process: Process?
    private var input: FileHandle?
    private var outputTask: Task<Void, Never>?
    private var pending: [Int: CheckedContinuation<[String: Any]?, Never>] = [:]
    private var outputBuffer = Data()
    private var nextRequestID = 0
    private var initialized = false

    func readLatest() async -> UsageSnapshot? {
        guard await ensureStarted(),
              let response = await sendRequest(method: "account/rateLimits/read") else {
            stop()
            return nil
        }
        return Self.parseResponse(response, capturedAt: Date())
    }

    func shutdown() {
        stop()
    }

    private func ensureStarted() async -> Bool {
        if initialized, process?.isRunning == true { return true }
        stop()
        guard let executable = Self.resolveExecutable() else { return false }

        let process = Process()
        let stdinPipe = Pipe()
        let stdoutPipe = Pipe()
        let stderrPipe = Pipe()
        process.executableURL = executable
        process.arguments = ["app-server", "--listen", "stdio://"]
        process.standardInput = stdinPipe
        process.standardOutput = stdoutPipe
        process.standardError = stderrPipe

        do {
            try process.run()
        } catch {
            return false
        }

        self.process = process
        input = stdinPipe.fileHandleForWriting
        outputTask = Task.detached(priority: .utility) { [weak self] in
            while !Task.isCancelled {
                let data = stdoutPipe.fileHandleForReading.availableData
                if data.isEmpty { break }
                await self?.consume(data)
            }
            await self?.connectionClosed()
        }
        Task.detached(priority: .background) {
            while !Task.isCancelled, !stderrPipe.fileHandleForReading.availableData.isEmpty {}
        }

        guard let response = await sendRequest(
            method: "initialize",
            parameters: [
                "clientInfo": [
                    "name": "codex_tempo_mac",
                    "title": "Codex Tempo",
                    "version": Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.1.0"
                ]
            ]
        ), response["result"] != nil else {
            stop()
            return false
        }
        guard sendNotification(method: "initialized", parameters: [:]) else {
            stop()
            return false
        }
        initialized = true
        return true
    }

    private func sendRequest(method: String, parameters: [String: Any]? = nil) async -> [String: Any]? {
        nextRequestID += 1
        let id = nextRequestID
        return await withCheckedContinuation { continuation in
            pending[id] = continuation
            var message: [String: Any] = ["method": method, "id": id]
            message["params"] = parameters ?? NSNull()
            guard write(message) else {
                pending.removeValue(forKey: id)?.resume(returning: nil)
                return
            }
            Task { [weak self] in
                try? await Task.sleep(nanoseconds: 8_000_000_000)
                await self?.timeout(id)
            }
        }
    }

    private func sendNotification(method: String, parameters: [String: Any]) -> Bool {
        write(["method": method, "params": parameters])
    }

    private func write(_ object: [String: Any]) -> Bool {
        guard JSONSerialization.isValidJSONObject(object),
              var data = try? JSONSerialization.data(withJSONObject: object),
              let input else { return false }
        data.append(0x0A)
        do {
            try input.write(contentsOf: data)
            return true
        } catch {
            return false
        }
    }

    private func consume(_ data: Data) {
        outputBuffer.append(data)
        while let newline = outputBuffer.firstIndex(of: 0x0A) {
            let line = outputBuffer[..<newline]
            outputBuffer.removeSubrange(...newline)
            guard let object = try? JSONSerialization.jsonObject(with: Data(line)) as? [String: Any],
                  let id = (object["id"] as? NSNumber)?.intValue,
                  let continuation = pending.removeValue(forKey: id) else { continue }
            continuation.resume(returning: object)
        }
    }

    private func timeout(_ id: Int) {
        pending.removeValue(forKey: id)?.resume(returning: nil)
    }

    private func connectionClosed() {
        initialized = false
        let continuations = pending.values
        pending.removeAll()
        continuations.forEach { $0.resume(returning: nil) }
    }

    private func stop() {
        initialized = false
        outputTask?.cancel()
        outputTask = nil
        try? input?.close()
        input = nil
        if let process, process.isRunning { process.terminate() }
        process = nil
        outputBuffer.removeAll(keepingCapacity: true)
        let continuations = pending.values
        pending.removeAll()
        continuations.forEach { $0.resume(returning: nil) }
    }

    static func parseResponse(_ response: [String: Any], capturedAt: Date) -> UsageSnapshot? {
        guard let result = response["result"] as? [String: Any] else { return nil }
        let limits: [String: Any]?
        if let buckets = result["rateLimitsByLimitId"] as? [String: Any],
           let canonical = buckets["codex"] as? [String: Any] {
            limits = canonical
        } else if let primary = result["rateLimits"] as? [String: Any],
                  let identifier = primary["limitId"] as? String,
                  identifier.caseInsensitiveCompare("codex") == .orderedSame {
            limits = primary
        } else {
            limits = nil
        }
        guard let limits else { return nil }
        let windows = CodexUsageReader.parseWindows(
            limits,
            usedKey: "usedPercent",
            durationKey: "windowDurationMins",
            resetKey: "resetsAt"
        )
        guard windows.five != nil || windows.week != nil else { return nil }
        return UsageSnapshot(
            fiveHour: windows.five,
            week: windows.week,
            capturedAt: capturedAt,
            source: sourceName
        )
    }

    private static func resolveExecutable(
        environment: [String: String] = ProcessInfo.processInfo.environment,
        home: URL = FileManager.default.homeDirectoryForCurrentUser
    ) -> URL? {
        let fileManager = FileManager.default
        var candidates: [URL] = []
        if let configured = environment["CODEX_EXECUTABLE"], !configured.isEmpty {
            candidates.append(URL(fileURLWithPath: configured))
        }
        if let path = environment["PATH"] {
            candidates.append(contentsOf: path.split(separator: ":").map {
                URL(fileURLWithPath: String($0)).appendingPathComponent("codex")
            })
        }
        candidates.append(home.appendingPathComponent(".local/bin/codex"))
        candidates.append(home.appendingPathComponent(".codex/packages/standalone/current/bin/codex"))
        candidates.append(URL(fileURLWithPath: "/Applications/Codex.app/Contents/Resources/codex"))
        candidates.append(URL(fileURLWithPath: "/Applications/Codex.app/Contents/MacOS/codex"))
        return candidates.first { fileManager.isExecutableFile(atPath: $0.path) }
    }
}
