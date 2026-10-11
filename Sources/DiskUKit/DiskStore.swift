import Foundation
import Observation

private struct LayoutKey: Equatable {
    let root: ObjectIdentifier
    let width: CGFloat
    let height: CGFloat
    let revision: Int
}

/// Owns the scanned tree and all UI state that refers into it. `FileNode` is not observable
/// itself; every tree mutation goes through here and bumps `revision`.
@MainActor @Observable
public final class DiskStore {
    public private(set) var state: ScanState = .idle
    public private(set) var revision = 0
    public var selection: FileNode? { didSet { if let selection { expandAncestors(of: selection) } } }
    public var zoomRoot: FileNode?
    public var hovered: FileNode?
    public var expanded: Set<FileNode> = []
    public private(set) var lastScanned: URL?
    /// Item awaiting the user's confirmation before it goes to the Trash.
    public var pendingTrash: FileNode?
    public var errorMessage: String?

    @ObservationIgnored private var scanTask: Task<Void, Never>?
    @ObservationIgnored private let trasher: (URL) throws -> Void
    @ObservationIgnored private var layoutCache: (key: LayoutKey, layout: TreemapLayout)?
    @ObservationIgnored private var kindTotalsCache: (revision: Int, totals: [(Kind, Int64)])?

    public init(trasher: @escaping (URL) throws -> Void = { try FileManager.default.trashItem(at: $0, resultingItemURL: nil) }) {
        self.trasher = trasher
    }

    public var root: FileNode? {
        if case .done(let root, _) = state { root } else { nil }
    }

    /// The folder the treemap currently shows.
    public var displayRoot: FileNode? { zoomRoot ?? root }

    public func scan(_ url: URL) {
        scanTask?.cancel()
        lastScanned = url
        selection = nil
        zoomRoot = nil
        hovered = nil
        expanded = []
        state = .scanning(progress: ScanProgress())
        scanTask = Task {
            do {
                let result = try await Scanner.scan(url) { [weak self] progress in
                    guard let self, case .scanning = self.state else { return }
                    self.state = .scanning(progress: progress)
                }
                self.finish(result)
            } catch is CancellationError {
                self.state = .idle
            } catch {
                self.state = .failed(error.localizedDescription)
            }
        }
    }

    /// Installs a finished scan; also the entry point for tools that scan without a task.
    public func finish(_ result: ScanResult) {
        state = .done(root: result.root, inaccessible: result.inaccessible)
        expanded = [result.root]
        revision += 1
    }

    public func cancelScan() {
        scanTask?.cancel()
        scanTask = nil
        state = .idle
    }

    public func rescan() {
        if let lastScanned { scan(lastScanned) }
    }

    public func zoom(into node: FileNode) {
        let folder = node.isFolder ? node : node.parent
        guard let folder, !folder.children.isEmpty else { return }
        zoomRoot = folder === root ? nil : folder
    }

    /// Moves the item to the Trash and removes it from the tree. Throws if the Trash refuses.
    public func trash(_ node: FileNode) throws {
        guard node !== root else { return }
        try trasher(node.url)
        let parent = node.parent
        if let zoomRoot, zoomRoot.isDescendant(of: node) { self.zoomRoot = parent === root ? nil : parent }
        if let selection, selection.isDescendant(of: node) { self.selection = parent }
        if let hovered, hovered.isDescendant(of: node) { self.hovered = nil }
        expanded.remove(node)
        node.removeFromParent()
        revision += 1
    }

    public func confirmPendingTrash() {
        guard let node = pendingTrash else { return }
        pendingTrash = nil
        do {
            try trash(node)
        } catch {
            errorMessage = "“\(node.name)” couldn’t be moved to the Trash. \(error.localizedDescription)"
        }
    }

    /// Layout for the current zoom root, recomputed only when the root, size, or tree changes.
    public func layout(for size: CGSize) -> TreemapLayout? {
        guard let displayRoot else { return nil }
        let key = LayoutKey(root: ObjectIdentifier(displayRoot), width: size.width, height: size.height, revision: revision)
        if let layoutCache, layoutCache.key == key { return layoutCache.layout }
        let layout = TreemapLayout(root: displayRoot, in: CGRect(origin: .zero, size: size))
        layoutCache = (key, layout)
        return layout
    }

    /// Bytes per kind across the whole scan, largest first.
    public var kindTotals: [(Kind, Int64)] {
        if let cache = kindTotalsCache, cache.revision == revision { return cache.totals }
        var totals: [Kind: Int64] = [:]
        var stack = root.map { [$0] } ?? []
        while let node = stack.popLast() {
            if node.children.isEmpty { totals[node.kind, default: 0] += node.size } else { stack.append(contentsOf: node.children) }
        }
        let sorted = totals.filter { $0.value > 0 }.sorted { $0.value > $1.value }.map { ($0.key, $0.value) }
        kindTotalsCache = (revision, sorted)
        return sorted
    }

    private func expandAncestors(of node: FileNode) {
        let missing = node.ancestors.filter { !expanded.contains($0) }
        if !missing.isEmpty { expanded.formUnion(missing) }
    }
}
