import SwiftUI

struct InspectorView: View {
    @Environment(DiskStore.self) private var store

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                if let node = store.selection {
                    ItemDetails(node: node, revision: store.revision)
                } else {
                    VStack(spacing: 6) {
                        Image(systemName: "cursorarrow.click.2")
                            .font(.system(size: 28))
                            .foregroundStyle(.tertiary)
                        Text("Select an item to inspect it")
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 24)
                }
                Divider()
                KindLegend()
            }
            .padding(16)
        }
    }
}

private struct ItemDetails: View {
    let node: FileNode
    let revision: Int
    @Environment(DiskStore.self) private var store

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .center, spacing: 12) {
                Image(nsImage: Icons.icon(for: node.url))
                    .resizable()
                    .frame(width: 64, height: 64)
                VStack(alignment: .leading, spacing: 4) {
                    Text(node.name)
                        .font(.title3.weight(.semibold))
                        .lineLimit(2)
                        .truncationMode(.middle)
                    Label {
                        Text(node.kind.label)
                    } icon: {
                        Circle().fill(node.kind.color).frame(width: 8, height: 8)
                    }
                    .font(.callout)
                    .foregroundStyle(.secondary)
                }
            }

            Text(Format.bytes(node.size))
                .font(.system(size: 30, weight: .bold, design: .rounded).monospacedDigit())

            Grid(alignment: .leading, horizontalSpacing: 10, verticalSpacing: 6) {
                if node.kind == .folder || node.kind == .bundle {
                    row("Contains", Format.items(node.itemCount))
                }
                if let parent = node.parent, parent.size > 0 {
                    row("Share", "\(Self.percent(node.size, of: parent.size)) of \(parent.name)")
                }
                if let modified {
                    row("Modified", modified.formatted(date: .abbreviated, time: .shortened))
                }
                row("Where", node.url.deletingLastPathComponent().path)
            }
            .font(.callout)

            VStack(spacing: 8) {
                HStack(spacing: 8) {
                    Button { Actions.reveal(node) } label: { Label("Reveal", systemImage: "folder") }
                    Button { Actions.quickLook(node) } label: { Label("Quick Look", systemImage: "eye") }
                    Button { Actions.open(node) } label: { Label("Open", systemImage: "arrow.up.forward.app") }
                }
                .buttonStyle(.bordered)
                .controlSize(.regular)

                Button(role: .destructive) {
                    store.pendingTrash = node
                } label: {
                    Label("Move to Trash…", systemImage: "trash").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .tint(.red)
                .controlSize(.large)
                .disabled(node === store.root)
            }
        }
    }

    private var modified: Date? {
        try? node.url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate
    }

    private func row(_ title: String, _ value: String) -> some View {
        GridRow(alignment: .firstTextBaseline) {
            Text(title).foregroundStyle(.secondary).gridColumnAlignment(.trailing)
            Text(value).textSelection(.enabled).lineLimit(3).truncationMode(.middle)
        }
    }

    static func percent(_ part: Int64, of whole: Int64) -> String {
        (Double(part) / Double(whole)).formatted(.percent.precision(.fractionLength(1)))
    }
}

struct KindLegend: View {
    @Environment(DiskStore.self) private var store

    var body: some View {
        let totals = store.kindTotals
        let largest = totals.first?.1 ?? 1
        VStack(alignment: .leading, spacing: 8) {
            Text("By Kind").font(.headline)
            ForEach(totals, id: \.0) { kind, bytes in
                VStack(alignment: .leading, spacing: 3) {
                    HStack {
                        RoundedRectangle(cornerRadius: 3).fill(kind.color).frame(width: 12, height: 12)
                        Text(kind.label).lineLimit(1)
                        Spacer()
                        Text(Format.bytes(bytes)).monospacedDigit().foregroundStyle(.secondary)
                    }
                    ProportionBar(fraction: Double(bytes) / Double(largest), color: kind.color)
                }
                .font(.callout)
            }
        }
    }
}
