import SwiftUI

struct OutlinePane: View {
    @Environment(DiskStore.self) private var store
    let root: FileNode

    var body: some View {
        @Bindable var store = store
        ScrollViewReader { proxy in
            List(selection: $store.selection) {
                OutlineRows(nodes: [root], revision: store.revision)
            }
            .listStyle(.sidebar)
            .onDeleteCommand { store.pendingTrash = store.selection }
            .onChange(of: store.selection) { _, node in
                guard let node else { return }
                withAnimation(.easeInOut(duration: 0.2)) { proxy.scrollTo(node.id) }
            }
        }
    }
}

private struct OutlineRows: View {
    @Environment(DiskStore.self) private var store
    let nodes: [FileNode]
    /// Sizes live on non-observable nodes, so rows re-render when the tree revision changes.
    let revision: Int

    var body: some View {
        ForEach(nodes) { node in
            if node.outlineChildren != nil {
                DisclosureGroup(isExpanded: expansion(node)) {
                    OutlineRows(nodes: node.children, revision: revision)
                } label: {
                    OutlineRow(node: node)
                }
                .tag(node)
            } else {
                OutlineRow(node: node).tag(node)
            }
        }
    }

    private func expansion(_ node: FileNode) -> Binding<Bool> {
        Binding(
            get: { store.expanded.contains(node) },
            set: { open in if open { store.expanded.insert(node) } else { store.expanded.remove(node) } }
        )
    }
}

struct OutlineRow: View {
    let node: FileNode

    var body: some View {
        HStack(spacing: 6) {
            Image(nsImage: Icons.icon(for: node.url))
                .resizable()
                .frame(width: 16, height: 16)
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(node.name).lineLimit(1).truncationMode(.middle)
                    Spacer(minLength: 6)
                    Text(Format.bytes(node.size))
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
                ProportionBar(fraction: fraction, color: node.kind.color)
            }
        }
        .padding(.vertical, 1)
        .contextMenu { ItemMenu(node: node) }
    }

    private var fraction: Double {
        guard let parent = node.parent, parent.size > 0 else { return 1 }
        return Double(node.size) / Double(parent.size)
    }
}

struct ProportionBar: View {
    let fraction: Double
    let color: Color

    var body: some View {
        Capsule()
            .fill(Color.primary.opacity(0.1))
            .frame(height: 3)
            .overlay(alignment: .leading) {
                GeometryReader { geometry in
                    Capsule().fill(color).frame(width: max(2, geometry.size.width * fraction))
                }
            }
    }
}

/// Shared by outline rows and the treemap so both offer the same actions.
struct ItemMenu: View {
    @Environment(DiskStore.self) private var store
    let node: FileNode

    var body: some View {
        Button("Reveal in Finder") { Actions.reveal(node) }
        Button("Quick Look") { Actions.quickLook(node) }
        Button("Copy Path") { Actions.copyPath(node) }
        if node.isFolder, !node.children.isEmpty {
            Button("Zoom Into Folder") { store.zoom(into: node) }
        }
        Divider()
        Button("Move to Trash…", role: .destructive) { store.pendingTrash = node }
            .disabled(node === store.root)
    }
}
