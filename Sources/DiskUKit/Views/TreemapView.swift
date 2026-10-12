import SwiftUI

struct TreemapView: View {
    @Environment(DiskStore.self) private var store

    var body: some View {
        GeometryReader { geometry in
            if let layout = store.layout(for: geometry.size) {
                ZStack {
                    TreemapCanvas(layout: layout, revision: store.revision, rootID: store.displayRoot.map(ObjectIdentifier.init),
                                  collapsed: store.collapsed)
                        .equatable()
                    HighlightCanvas(layout: layout, selection: store.selection, hovered: store.hovered)
                }
                .contentShape(Rectangle())
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let point): store.hovered = layout.tile(at: point)?.node
                    case .ended: store.hovered = nil
                    }
                }
                .gesture(SpatialTapGesture().onEnded { value in
                    if let folder = layout.folderHeader(at: value.location), folder !== store.displayRoot {
                        store.toggleCollapsed(folder)
                        store.selection = folder
                    } else {
                        store.selection = layout.tile(at: value.location)?.node
                    }
                })
                .simultaneousGesture(SpatialTapGesture(count: 2).onEnded { value in
                    if let tile = layout.tile(at: value.location) { store.zoom(into: tile.node) }
                })
                .contextMenu {
                    if let node = store.hovered ?? store.selection {
                        ItemMenu(node: node)
                    }
                }
            }
        }
    }
}

/// The expensive layer: redrawn only when the layout itself changes, not on hover or selection.
private struct TreemapCanvas: View, Equatable {
    let layout: TreemapLayout
    let revision: Int
    let rootID: ObjectIdentifier?
    let collapsed: Set<FileNode>

    static func == (a: Self, b: Self) -> Bool {
        a.revision == b.revision && a.rootID == b.rootID && a.collapsed == b.collapsed
            && a.layout.tiles.first?.rect == b.layout.tiles.first?.rect
    }

    var body: some View {
        Canvas(rendersAsynchronously: true) { context, _ in
            for tile in layout.tiles { draw(tile, in: &context) }
        }
    }

    private func draw(_ tile: TreemapTile, in context: inout GraphicsContext) {
        let rect = tile.rect
        switch tile.role {
        case .folder(let header):
            context.fill(Path(rect), with: .color(Palette.folderFill(depth: tile.depth)))
            drawHeader(header, of: tile, open: true, context: &context)
        case .collapsed(let header):
            cushion(rect, kind: nil, context: &context)
            drawHeader(header, of: tile, open: false, context: &context)
            let body = header.map { rect.divided(atDistance: $0.maxY - rect.minY, from: .minYEdge).remainder } ?? rect
            if body.width > 56, body.height > 30 {
                drawLabel(Format.items(tile.node.itemCount), detail: "Click the title to expand",
                          in: body.insetBy(dx: 6, dy: 5), size: 11, weight: .medium, context: &context, stacked: true)
            }
        case .leaf:
            let kind = tile.node.isFolder ? nil : tile.node.kind
            cushion(rect, kind: kind, context: &context)
            if rect.width > 56, rect.height > 30 {
                drawLabel(tile.node.name, detail: Format.bytes(tile.node.size),
                          in: rect.insetBy(dx: 5, dy: 4), size: 11, weight: .medium, context: &context, stacked: true)
            }
        case .remainder:
            context.fill(Path(rect), with: .color(Palette.remainder))
        }
    }

    private func drawHeader(_ header: CGRect?, of tile: TreemapTile, open: Bool, context: inout GraphicsContext) {
        guard let header else { return }
        context.fill(Path(header), with: .color(Palette.headerFill(depth: tile.depth)))
        let name = tile.depth == 0 ? tile.node.name : "\(open ? "▾" : "▸") \(tile.node.name)"
        drawLabel(name, detail: Format.bytes(tile.node.size),
                  in: header.insetBy(dx: 4, dy: 0), size: 10.5, weight: .semibold, context: &context)
    }

    /// Cushion-like shading: a diagonal highlight-to-shadow ramp of the kind color.
    private func cushion(_ rect: CGRect, kind: Kind?, context: inout GraphicsContext) {
        let path = Path(rect)
        if rect.width < 3 || rect.height < 3 {
            context.fill(path, with: .color(kind?.color ?? Palette.emptyFolder))
            return
        }
        let gradient = kind.map { Palette.cushions[$0]! } ?? Palette.emptyCushion
        context.fill(path, with: .linearGradient(gradient, startPoint: CGPoint(x: rect.minX, y: rect.minY),
                                                 endPoint: CGPoint(x: rect.maxX, y: rect.maxY)))
        context.stroke(Path(rect.insetBy(dx: 0.25, dy: 0.25)), with: .color(.black.opacity(0.35)), lineWidth: 0.5)
    }

    private func drawLabel(_ name: String, detail: String, in rect: CGRect, size: CGFloat, weight: Font.Weight,
                           context: inout GraphicsContext, stacked: Bool = false) {
        context.drawLayer { layer in
            layer.clip(to: Path(rect))
            let title = layer.resolve(Text(name).font(.system(size: size, weight: weight)).foregroundColor(.white))
            let sub = layer.resolve(Text(detail).font(.system(size: size - 1).monospacedDigit()).foregroundColor(.white.opacity(0.8)))
            layer.addFilter(.shadow(color: .black.opacity(0.6), radius: 1, x: 0, y: 0.5))
            if stacked {
                layer.draw(title, at: CGPoint(x: rect.minX, y: rect.minY), anchor: .topLeading)
                if rect.height > 30 {
                    layer.draw(sub, at: CGPoint(x: rect.minX, y: rect.minY + size + 3), anchor: .topLeading)
                }
            } else {
                let titleWidth = title.measure(in: rect.size).width
                layer.draw(title, at: CGPoint(x: rect.minX, y: rect.midY), anchor: .leading)
                if titleWidth + 60 < rect.width {
                    layer.draw(sub, at: CGPoint(x: rect.maxX, y: rect.midY), anchor: .trailing)
                }
            }
        }
    }
}

private struct HighlightCanvas: View {
    let layout: TreemapLayout
    let selection: FileNode?
    let hovered: FileNode?

    var body: some View {
        Canvas { context, _ in
            if let hovered, hovered !== selection, let rect = layout.rect(of: hovered) {
                context.stroke(Path(rect.insetBy(dx: 0.5, dy: 0.5)), with: .color(.white.opacity(0.7)), lineWidth: 1)
            }
            if let selection, let rect = layout.rect(of: selection) {
                context.stroke(Path(rect.insetBy(dx: 1.5, dy: 1.5)), with: .color(.black.opacity(0.6)), lineWidth: 3)
                context.stroke(Path(rect.insetBy(dx: 1.5, dy: 1.5)), with: .color(.white), lineWidth: 1.5)
            }
        }
        .allowsHitTesting(false)
    }
}

enum Palette {
    static let emptyFolder = Color(white: 0.35)
    static let remainder = Color(white: 0.30)
    static let cushions = Dictionary(uniqueKeysWithValues: Kind.allCases.map { ($0, cushion($0.color)) })
    static let emptyCushion = cushion(emptyFolder)

    private static func cushion(_ color: Color) -> Gradient {
        Gradient(stops: [
            .init(color: color.blended(with: .white, 0.35), location: 0),
            .init(color: color, location: 0.45),
            .init(color: color.blended(with: .black, 0.45), location: 1),
        ])
    }

    static func folderFill(depth: Int) -> Color { Color(white: max(0.10, 0.20 - Double(depth) * 0.02)) }
    static func headerFill(depth: Int) -> Color { Color(white: max(0.16, 0.28 - Double(depth) * 0.025)) }
}

extension Color {
    func blended(with other: NSColor, _ fraction: CGFloat) -> Color {
        Color(nsColor: NSColor(self).usingColorSpace(.sRGB)?.blended(withFraction: fraction, of: other) ?? NSColor(self))
    }
}
