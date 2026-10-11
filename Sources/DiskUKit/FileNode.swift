import Foundation

/// One entry in the scanned tree. Directory sizes are always the sum of their children,
/// maintained solely by `propagate(size:items:)`.
///
/// `@unchecked Sendable`: a subtree is built by exactly one scanner task and handed to its
/// parent only after that task finishes; after the scan, all mutation happens on the main actor.
public final class FileNode: Identifiable, Hashable, @unchecked Sendable {
    public let url: URL
    public let name: String
    public let kind: Kind
    public private(set) var size: Int64
    /// Regular files (and symlinks) in this subtree; a file counts itself.
    public private(set) var itemCount: Int
    public private(set) var children: [FileNode] = []
    public private(set) weak var parent: FileNode?

    public init(url: URL, kind: Kind, size: Int64 = 0, itemCount: Int = 0) {
        self.url = url
        self.name = url.lastPathComponent.isEmpty ? url.path : url.lastPathComponent
        self.kind = kind
        self.size = size
        self.itemCount = itemCount
    }

    public var isFolder: Bool { kind == .folder }

    /// Children for disclosure in the outline; nil for leaves so no chevron is shown.
    public var outlineChildren: [FileNode]? { isFolder && !children.isEmpty ? children : nil }

    public var ancestors: [FileNode] {
        var chain: [FileNode] = []
        var node = parent
        while let n = node { chain.append(n); node = n.parent }
        return chain
    }

    public func isDescendant(of other: FileNode) -> Bool {
        self === other || ancestors.contains { $0 === other }
    }

    /// Attaches `newChildren` and folds their totals into this node and its ancestors.
    public func adopt(_ newChildren: [FileNode]) {
        var addedSize: Int64 = 0
        var addedItems = 0
        for child in newChildren {
            child.parent = self
            addedSize += child.size
            addedItems += child.itemCount
        }
        children.append(contentsOf: newChildren)
        children.sort(by: FileNode.largerFirst)
        propagate(size: addedSize, items: addedItems)
    }

    /// Detaches this node and subtracts its totals from every ancestor.
    public func removeFromParent() {
        guard let parent else { return }
        parent.children.removeAll { $0 === self }
        parent.propagate(size: -size, items: -itemCount)
        self.parent = nil
    }

    /// The single place sizes change: applies a delta to this node and every ancestor,
    /// keeping each sibling list sorted size-descending.
    func propagate(size delta: Int64, items itemDelta: Int) {
        var node: FileNode? = self
        while let n = node {
            n.size += delta
            n.itemCount += itemDelta
            if let p = n.parent, delta != 0 { p.reposition(n) }
            node = n.parent
        }
    }

    private func reposition(_ child: FileNode) {
        guard let index = children.firstIndex(where: { $0 === child }) else { return }
        children.remove(at: index)
        let insertAt = children.firstIndex { FileNode.largerFirst(child, $0) } ?? children.endIndex
        children.insert(child, at: insertAt)
    }

    static func largerFirst(_ a: FileNode, _ b: FileNode) -> Bool {
        a.size != b.size ? a.size > b.size : a.name < b.name
    }

    public static func == (a: FileNode, b: FileNode) -> Bool { a === b }
    public func hash(into hasher: inout Hasher) { hasher.combine(ObjectIdentifier(self)) }
}
