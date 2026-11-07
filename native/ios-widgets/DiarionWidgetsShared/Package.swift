// swift-tools-version: 5.9
// The data side of the iOS widgets (spec 20, R6): the snapshot the app writes into the App Group,
// decoded; the staleness rule; sample data for the gallery. Foundation only, so `swift test` runs on
// the macOS runner without a simulator — a decode mistake costs a minute there, not a TestFlight round.
import PackageDescription

let package = Package(
    name: "DiarionWidgetsShared",
    platforms: [.iOS(.v16), .macOS(.v13)],
    products: [
        .library(name: "DiarionWidgetsShared", targets: ["DiarionWidgetsShared"])
    ],
    targets: [
        .target(name: "DiarionWidgetsShared"),
        .testTarget(name: "DiarionWidgetsSharedTests", dependencies: ["DiarionWidgetsShared"])
    ]
)
