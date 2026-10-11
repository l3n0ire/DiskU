import CoreGraphics

public struct TreemapTile {
    public enum Role { case folder(header: CGRect?), leaf, remainder }

    /// For `.remainder`, the folder whose sub-pixel children were folded into this tile.
    public let node: FileNode
    public let rect: CGRect
    public let depth: Int
    public let role: Role
}

/// Flattened nested treemap in paint order: every folder precedes its contents,
/// so the last tile containing a point is the deepest one.
public struct TreemapLayout {
    public static let headerHeight: CGFloat = 15
    static let inset: CGFloat = 2
    /// Children whose area would be below this many square points are not drawn individually.
    static let minArea: CGFloat = 1

    public let tiles: [TreemapTile]

    public init(root: FileNode, in bounds: CGRect) {
        var tiles: [TreemapTile] = []
        Self.place(root, in: bounds, depth: 0, into: &tiles)
        self.tiles = tiles
    }

    public func tile(at point: CGPoint) -> TreemapTile? {
        tiles.last { $0.rect.contains(point) }
    }

    public func rect(of node: FileNode) -> CGRect? {
        tiles.first { $0.node === node && !$0.isRemainder }?.rect
    }

    private static func place(_ node: FileNode, in rect: CGRect, depth: Int, into tiles: inout [TreemapTile]) {
        guard rect.width >= 1, rect.height >= 1 else { return }
        guard node.isFolder, !node.children.isEmpty, node.size > 0 else {
            tiles.append(TreemapTile(node: node, rect: rect, depth: depth, role: .leaf))
            return
        }

        var content = rect
        var header: CGRect?
        if rect.width > 4 * inset, rect.height > 4 * inset {
            content = rect.insetBy(dx: inset, dy: inset)
            if content.height > headerHeight * 2.5, content.width > 40 {
                header = CGRect(x: content.minX, y: content.minY, width: content.width, height: headerHeight)
                content = content.divided(atDistance: headerHeight, from: .minYEdge).remainder
            }
        }
        tiles.append(TreemapTile(node: node, rect: rect, depth: depth, role: .folder(header: header)))

        let scale = Double(content.width * content.height) / Double(node.size)
        let visible = node.children.prefix { Double($0.size) * scale >= Double(minArea) }
        var sizes = visible.map { Double($0.size) }
        let rest = Double(node.size) - sizes.reduce(0, +)
        if rest > 0 { sizes.append(rest) }

        let rects = squarify(sizes: sizes, in: content)
        for (child, childRect) in zip(visible, rects) {
            place(child, in: childRect, depth: depth + 1, into: &tiles)
        }
        if rest > 0, let restRect = rects.last, restRect.width >= 1, restRect.height >= 1 {
            tiles.append(TreemapTile(node: node, rect: restRect, depth: depth + 1, role: .remainder))
        }
    }
}

extension TreemapTile {
    public var isRemainder: Bool { if case .remainder = role { true } else { false } }
}
