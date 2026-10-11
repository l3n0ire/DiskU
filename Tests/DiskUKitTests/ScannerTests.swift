import Foundation
import Testing
@testable import DiskUKit

/// Builds a fixture whose allocated sizes are exact: APFS allocates 4 KiB blocks and every file
/// is a whole number of blocks of incompressible-looking data.
func makeFixture() throws -> URL {
    let fm = FileManager.default
    let root = fm.temporaryDirectory.appendingPathComponent("DiskUTests-\(UUID().uuidString)")
    func write(_ path: String, blocks: Int) throws {
        let url = root.appendingPathComponent(path)
        try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let bytes = (0..<(blocks * 4096)).map { UInt8(truncatingIfNeeded: $0 &* 2654435761 >> 7) }
        try Data(bytes).write(to: url)
    }
    try write("movie.mov", blocks: 5)
    try write("photos/a.png", blocks: 2)
    try write("photos/b.jpg", blocks: 1)
    try write("photos/deep/notes.txt", blocks: 1)
    try write("Tool.app/Contents/MacOS/Tool", blocks: 3)
    try write("Tool.app/Contents/Info.plist", blocks: 1)
    try fm.createDirectory(at: root.appendingPathComponent("empty"), withIntermediateDirectories: true)
    try fm.createSymbolicLink(atPath: root.appendingPathComponent("photos/deep/loop").path, withDestinationPath: "../..")
    try fm.createSymbolicLink(atPath: root.appendingPathComponent("movie-alias.mov").path, withDestinationPath: "movie.mov")
    return root
}

@Test func scanTotalsFixtureExactlyWithoutFollowingSymlinks() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }

    let result = try await Scanner.scan(root)
    let tree = result.root

    #expect(tree.size == 13 * 4096)
    #expect(tree.itemCount == 8)
    #expect(result.inaccessible == 0)
    #expect(tree.children.map(\.name) == ["movie.mov", "Tool.app", "photos", "empty", "movie-alias.mov"])
    #expect(tree.children.map(\.size) == [20480, 16384, 16384, 0, 0])

    let photos = try #require(tree.children.first { $0.name == "photos" })
    #expect(photos.kind == .folder)
    #expect(photos.size == 4 * 4096)
    #expect(photos.children.map(\.name) == ["a.png", "b.jpg", "deep"])

    let app = try #require(tree.children.first { $0.name == "Tool.app" })
    #expect(app.kind == .bundle)
    #expect(app.itemCount == 2)
    #expect(app.children.isEmpty)

    let loop = try #require(photos.children.first { $0.name == "deep" }?.children.first { $0.name == "loop" })
    #expect(loop.kind == .other)
    #expect(loop.size == 0)
    #expect(tree.children.first { $0.name == "movie.mov" }?.kind == .video)
}

@Test func unreadableDirectoryCountsAsInaccessible() async throws {
    let root = try makeFixture()
    let locked = root.appendingPathComponent("photos/deep")
    try FileManager.default.setAttributes([.posixPermissions: 0o000], ofItemAtPath: locked.path)
    defer {
        try? FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: locked.path)
        try? FileManager.default.removeItem(at: root)
    }

    let result = try await Scanner.scan(root)
    #expect(result.inaccessible == 1)
    #expect(result.root.size == 12 * 4096)
}

@Test func scanningAFileFails() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }
    await #expect(throws: (any Error).self) {
        try await Scanner.scan(root.appendingPathComponent("movie.mov"))
    }
}

@MainActor
@Test func progressReportsFinalCounts() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }
    var last = ScanProgress()
    _ = try await Scanner.scan(root) { last = $0 }
    #expect(last.filesSeen == 8)
    #expect(last.bytesSeen == 13 * 4096)
}

@Test func cancelledScanThrowsCancellation() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }
    let task = Task { try await Scanner.scan(root) }
    task.cancel()
    await #expect(throws: CancellationError.self) { try await task.value }
}
