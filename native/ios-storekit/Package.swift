// swift-tools-version:5.9
// The StoreKit 2 facade Diarion P/Invokes on iOS (spec 15, R3). Built into a static xcframework by
// build-xcframework.sh; the package itself exists so the facade has a module, a test target and
// one place that says which iOS it needs.
import PackageDescription

let package = Package(
    name: "DiarionStoreKit",
    platforms: [.iOS(.v16)],
    products: [
        .library(name: "DiarionStoreKit", type: .static, targets: ["DiarionStoreKit"])
    ],
    targets: [
        .target(
            name: "DiarionStoreKit",
            path: "Sources/DiarionStoreKit"
        ),
        .testTarget(
            name: "DiarionStoreKitTests",
            dependencies: ["DiarionStoreKit"],
            path: "Tests/DiarionStoreKitTests",
            resources: [.copy("Diarion.storekit")]
        )
    ]
)
