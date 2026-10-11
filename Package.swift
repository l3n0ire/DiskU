// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DiskU",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "DiskU", targets: ["DiskU"]),
        .executable(name: "diskusnap", targets: ["diskusnap"]),
    ],
    targets: [
        .target(name: "DiskUKit"),
        .executableTarget(name: "DiskU", dependencies: ["DiskUKit"]),
        .executableTarget(name: "diskusnap", dependencies: ["DiskUKit"]),
        .testTarget(name: "DiskUKitTests", dependencies: ["DiskUKit"]),
    ],
    swiftLanguageModes: [.v5]
)
