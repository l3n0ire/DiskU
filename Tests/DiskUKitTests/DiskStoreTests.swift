import Foundation
import Testing
@testable import DiskUKit

@MainActor
@Test func trashingRemovesNodeAndUpdatesAncestorsAndSelection() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }
    var trashed: [String] = []
    let store = DiskStore(trasher: { trashed.append($0.lastPathComponent) })
    store.finish(try await Scanner.scan(root))

    let tree = try #require(store.root)
    let photos = try #require(tree.children.first { $0.name == "photos" })
    let png = try #require(photos.children.first { $0.name == "a.png" })
    store.selection = png
    store.zoom(into: photos)
    let revision = store.revision

    store.pendingTrash = png
    store.confirmPendingTrash()

    #expect(trashed == ["a.png"])
    #expect(store.pendingTrash == nil)
    #expect(store.errorMessage == nil)
    #expect(store.revision == revision + 1)
    #expect(photos.size == 2 * 4096)
    #expect(tree.size == 11 * 4096)
    #expect(tree.itemCount == 7)
    #expect(photos.children.map(\.name) == ["b.jpg", "deep"])
    #expect(tree.children.map(\.name) == ["movie.mov", "Tool.app", "photos", "empty", "movie-alias.mov"])
    #expect(store.selection === photos)
    #expect(store.zoomRoot === photos)
}

@MainActor
@Test func trashingZoomedFolderZoomsOutAndRefusalSurfacesError() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }
    var refuse = false
    let store = DiskStore(trasher: { url in
        if refuse { throw CocoaError(.fileWriteNoPermission, userInfo: [NSFilePathErrorKey: url.path]) }
    })
    store.finish(try await Scanner.scan(root))
    let tree = try #require(store.root)
    let photos = try #require(tree.children.first { $0.name == "photos" })
    let deep = try #require(photos.children.first { $0.name == "deep" })
    store.zoom(into: deep)
    #expect(store.zoomRoot === deep)

    store.pendingTrash = photos
    store.confirmPendingTrash()
    #expect(store.zoomRoot == nil)
    #expect(tree.size == 9 * 4096)
    #expect(tree.children.map(\.name) == ["movie.mov", "Tool.app", "empty", "movie-alias.mov"])

    refuse = true
    let movie = try #require(tree.children.first)
    store.pendingTrash = movie
    store.confirmPendingTrash()
    #expect(store.errorMessage?.hasPrefix("“movie.mov” couldn’t be moved to the Trash.") == true)
    #expect(tree.size == 9 * 4096)
    #expect(movie.parent === tree)
}

@MainActor
@Test func layoutTilesAreProportionalAndHitTestable() async throws {
    let root = try makeFixture()
    defer { try? FileManager.default.removeItem(at: root) }
    let store = DiskStore(trasher: { _ in })
    store.finish(try await Scanner.scan(root))
    let size = CGSize(width: 400, height: 300)

    let first = try #require(store.layout(for: size))
    #expect(first.tiles.first?.rect == CGRect(x: 0, y: 0, width: 400, height: 300))
    let movie = try #require(first.tiles.first { $0.node.name == "movie.mov" })
    #expect(abs(movie.rect.width * movie.rect.height - 5.0 / 13.0 * 396 * 281) < 0.5)
    #expect(first.tile(at: CGPoint(x: movie.rect.midX, y: movie.rect.midY))?.node.name == "movie.mov")
}

@MainActor
@Test func cancelledScanDoesNotOverwriteItsReplacement() async throws {
    let first = try makeFixture()
    let second = try makeFixture()
    defer { for dir in [first, second] { try? FileManager.default.removeItem(at: dir) } }
    let store = DiskStore(trasher: { _ in })

    store.scan(first)
    store.cancelScan()
    store.scan(second)
    let deadline = ContinuousClock.now + .seconds(5)
    while case .scanning = store.state, ContinuousClock.now < deadline {
        try await Task.sleep(for: .milliseconds(10))
    }

    let scanned = try #require(store.root)
    #expect(scanned.url.standardizedFileURL == second.standardizedFileURL)
    #expect(scanned.size == 13 * 4096)
}

@MainActor
@Test func outlineListsLargestChildrenFirstAndRevealsHiddenSelections() async throws {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("DiskUOutline-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: root) }
    let many = root.appendingPathComponent("many")
    try FileManager.default.createDirectory(at: many, withIntermediateDirectories: true)
    for i in 1...150 {
        try Data(count: i * 4096).write(to: many.appendingPathComponent("f\(i).bin"))
    }
    let store = DiskStore(trasher: { _ in })
    store.finish(try await Scanner.scan(root))
    let folder = try #require(store.root?.children.first)

    #expect(store.outlineLimit(for: folder) == 100)
    #expect(folder.children.prefix(100).last?.name == "f51.bin")

    let hidden = folder.children[119]
    #expect(hidden.name == "f31.bin")
    store.selection = hidden
    #expect(store.outlineLimit(for: folder) == 120)

    store.showMore(in: folder)
    #expect(store.outlineLimit(for: folder) == 620)

    store.scan(root)
    #expect(store.outlineLimit(for: folder) == 100)
    store.cancelScan()
}
