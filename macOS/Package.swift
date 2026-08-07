// swift-tools-version: 5.10

import PackageDescription

let package = Package(
    name: "CodexTempoMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "CodexTempo", targets: ["CodexTempoMac"])
    ],
    targets: [
        .executableTarget(name: "CodexTempoMac")
    ]
)
