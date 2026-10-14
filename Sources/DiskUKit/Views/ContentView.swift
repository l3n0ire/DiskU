import SwiftUI

public struct ContentView: View {
    @Environment(DiskStore.self) private var store

    public init() {}

    public var body: some View {
        @Bindable var store = store
        Group {
            switch store.state {
            case .idle:
                WelcomeView()
            case .scanning(let progress):
                ScanningView(progress: progress)
            case .failed(let message):
                FailureView(message: message)
            case .done(let root, let inaccessible):
                BrowserView(root: root, inaccessible: inaccessible)
            }
        }
        .safeAreaInset(edge: .top, spacing: 0) { UpdateBanner() }
        .frame(minWidth: 900, minHeight: 560)
        .toolbar { ScanToolbar() }
        .onKeyPress(.space) {
            guard let node = store.selection else { return .ignored }
            Actions.quickLook(node)
            return .handled
        }
        .onKeyPress(keys: [.delete, .deleteForward]) { _ in
            guard let node = store.selection, node !== store.root else { return .ignored }
            store.pendingTrash = node
            return .handled
        }
        .confirmationDialog(
            trashTitle,
            isPresented: Binding(get: { store.pendingTrash != nil }, set: { if !$0 { store.pendingTrash = nil } }),
            titleVisibility: .visible,
            presenting: store.pendingTrash
        ) { _ in
            Button("Move to Trash", role: .destructive) { store.confirmPendingTrash() }
            Button("Cancel", role: .cancel) { store.pendingTrash = nil }
        } message: { node in
            Text("This frees about \(Format.bytes(node.size)). You can put it back from the Trash in Finder.\n\(node.url.path)")
        }
        .alert(
            "Couldn’t Move to Trash",
            isPresented: Binding(get: { store.errorMessage != nil }, set: { if !$0 { store.errorMessage = nil } }),
            presenting: store.errorMessage
        ) { _ in
            Button("OK") { store.errorMessage = nil }
        } message: { message in
            Text(message)
        }
    }

    private var trashTitle: String {
        guard let node = store.pendingTrash else { return "" }
        return "Move “\(node.name)” (\(Format.bytes(node.size))) to the Trash?"
    }
}

/// The three-pane browser. Public so `diskusnap` renders exactly what the app shows.
public struct BrowserView: View {
    @Environment(DiskStore.self) private var store
    let root: FileNode
    let inaccessible: Int
    @State private var showInspector = true

    public init(root: FileNode, inaccessible: Int) {
        self.root = root
        self.inaccessible = inaccessible
    }

    public var body: some View {
        NavigationSplitView {
            OutlinePane(root: root)
                .navigationSplitViewColumnWidth(min: 240, ideal: 300, max: 480)
        } detail: {
            TreemapPane(inaccessible: inaccessible)
        }
        .inspector(isPresented: $showInspector) {
            InspectorView()
                .inspectorColumnWidth(min: 250, ideal: 290, max: 400)
        }
    }
}

public struct TreemapPane: View {
    @Environment(DiskStore.self) private var store
    let inaccessible: Int

    public init(inaccessible: Int) { self.inaccessible = inaccessible }

    public var body: some View {
        VStack(spacing: 0) {
            if inaccessible > 0 {
                FullDiskAccessBanner(count: inaccessible)
            }
            Breadcrumbs()
            Divider()
            TreemapView()
                .background(Color(white: 0.08))
            Divider()
            StatusBar()
        }
    }
}

struct Breadcrumbs: View {
    @Environment(DiskStore.self) private var store

    var body: some View {
        let trail = store.displayRoot.map { [$0] + $0.ancestors } ?? []
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 2) {
                ForEach(Array(trail.reversed().enumerated()), id: \.element.id) { index, node in
                    if index > 0 {
                        Image(systemName: "chevron.right").font(.caption2.weight(.semibold)).foregroundStyle(.tertiary)
                    }
                    HStack(spacing: 4) {
                        Image(nsImage: Icons.icon(for: node.url)).resizable().frame(width: 14, height: 14)
                        Text(node.name)
                    }
                    .padding(.horizontal, 6)
                    .padding(.vertical, 3)
                    .background(node === store.displayRoot ? Color.primary.opacity(0.12) : Color.clear,
                                in: RoundedRectangle(cornerRadius: 5))
                    .contentShape(Rectangle())
                    .onTapGesture { store.zoomRoot = node === store.root ? nil : node }
                }
                if let zoomRoot = store.zoomRoot {
                    Text(Format.bytes(zoomRoot.size))
                        .foregroundStyle(.secondary)
                        .padding(.leading, 8)
                }
            }
            .font(.callout)
            .padding(.horizontal, 10)
            .padding(.vertical, 6)
        }
    }
}

struct StatusBar: View {
    @Environment(DiskStore.self) private var store

    var body: some View {
        HStack(spacing: 8) {
            if let node = store.hovered ?? store.selection {
                Circle().fill(node.kind.color).frame(width: 8, height: 8)
                Text(node.url.path).lineLimit(1).truncationMode(.middle)
                Spacer(minLength: 12)
                Text(Format.bytes(node.size)).monospacedDigit()
            } else if let root = store.displayRoot {
                Text("Click to select · Double-click a folder to zoom in · Space to Quick Look")
                    .foregroundStyle(.secondary)
                Spacer()
                Text("\(Format.items(root.itemCount)) · \(Format.bytes(root.size))").monospacedDigit()
            }
        }
        .font(.callout)
        .padding(.horizontal, 10)
        .frame(height: 26)
    }
}

struct FullDiskAccessBanner: View {
    let count: Int

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "lock.shield.fill").foregroundStyle(.orange).font(.title3)
            VStack(alignment: .leading, spacing: 1) {
                Text("\(count.formatted()) \(count == 1 ? "folder" : "folders") couldn’t be read").fontWeight(.semibold)
                Text("Sizes are incomplete. Grant DiskU Full Disk Access in System Settings, then rescan.")
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Button("Open Privacy Settings") { Actions.openFullDiskAccessSettings() }
        }
        .font(.callout)
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(.orange.opacity(0.12))
    }
}

struct FullDiskAccessCard: View {
    @State private var granted = Actions.hasFullDiskAccess

    var body: some View {
        Group {
            if granted {
                Label("Full Disk Access is on. DiskU can read every folder without asking.", systemImage: "checkmark.shield.fill")
                    .foregroundStyle(.green)
            } else {
                VStack(spacing: 10) {
                    Label("Allow Full Disk Access", systemImage: "lock.shield.fill")
                        .font(.headline)
                    Text("Scan your disk without the permission prompts.")
                        .font(.callout)
                    VStack(alignment: .leading, spacing: 4) {
                        step(1, "Click **Open Full Disk Access Settings**.")
                        step(2, "Turn on DiskU. If it isn’t listed, click **Show DiskU in Finder** and drag it into the list.")
                    }
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: 310, alignment: .leading)
                    HStack {
                        Button("Open Full Disk Access Settings") { Actions.openFullDiskAccessSettings() }
                            .buttonStyle(.borderedProminent)
                        Button("Show DiskU in Finder") { Actions.revealAppBundle() }
                    }
                }
                .padding(18)
                .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
            }
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            granted = Actions.hasFullDiskAccess
        }
    }
}

extension FullDiskAccessCard {
    /// Number in its own column so wrapped lines indent under the text, not the number.
    private func step(_ number: Int, _ text: LocalizedStringKey) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            Text("\(number).").monospacedDigit()
            Text(text)
        }
    }
}

struct UpdateBanner: View {
    @Environment(UpdateChecker.self) private var updates: UpdateChecker?

    var body: some View {
        if let updates, let release = updates.available, !updates.dismissed {
            HStack(spacing: 10) {
                Image(systemName: "arrow.down.circle.fill").foregroundStyle(.tint)
                Text("DiskU \(release.version.description) is available.").fontWeight(.medium)
                Spacer()
                Button("View Release") { NSWorkspace.shared.open(release.url) }
                Button { updates.dismissed = true } label: { Image(systemName: "xmark") }
                    .buttonStyle(.borderless)
                    .help("Dismiss")
            }
            .font(.callout)
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(.tint.opacity(0.12))
        }
    }
}

struct ScanToolbar: ToolbarContent {
    @Environment(DiskStore.self) private var store

    var body: some ToolbarContent {
        ToolbarItemGroup(placement: .primaryAction) {
            if case .scanning = store.state {
                Button { store.cancelScan() } label: { Label("Cancel", systemImage: "xmark.circle") }
            } else {
                Button { store.rescan() } label: { Label("Rescan", systemImage: "arrow.clockwise") }
                    .disabled(store.lastScanned == nil)
            }
            Button { store.scan(FileManager.default.homeDirectoryForCurrentUser) } label: {
                Label("Home", systemImage: "house")
            }
            Button { store.scan(URL(fileURLWithPath: "/")) } label: {
                Label("Full Disk", systemImage: "internaldrive")
            }
            Button { if let url = Actions.chooseFolder() { store.scan(url) } } label: {
                Label("Scan…", systemImage: "folder.badge.gearshape")
            }
        }
    }
}

struct WelcomeView: View {
    @Environment(DiskStore.self) private var store

    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "square.grid.3x3.square")
                .font(.system(size: 64, weight: .light))
                .foregroundStyle(.tint)
            Text("See what’s filling your disk").font(.largeTitle.weight(.semibold))
            Text("DiskU scans a folder and draws every file as a rectangle sized by the space it uses.")
                .foregroundStyle(.secondary)
            HStack(spacing: 12) {
                Button { store.scan(FileManager.default.homeDirectoryForCurrentUser) } label: {
                    Label("Scan Home Folder", systemImage: "house").frame(minWidth: 150)
                }
                .buttonStyle(.borderedProminent)
                Button { store.scan(URL(fileURLWithPath: "/")) } label: {
                    Label("Scan Full Disk", systemImage: "internaldrive").frame(minWidth: 150)
                }
                Button { if let url = Actions.chooseFolder() { store.scan(url) } } label: {
                    Label("Choose Folder…", systemImage: "folder").frame(minWidth: 150)
                }
            }
            .controlSize(.large)
            FullDiskAccessCard()
                .padding(.top, 12)
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

struct ScanningView: View {
    @Environment(DiskStore.self) private var store
    let progress: ScanProgress

    var body: some View {
        VStack(spacing: 16) {
            ProgressView().controlSize(.large)
            Text("Scanning \(store.lastScanned?.path ?? "")").font(.title2.weight(.semibold))
            HStack(spacing: 28) {
                stat(progress.filesSeen.formatted(), "files")
                stat(Format.bytes(progress.bytesSeen), "found")
            }
            Text(progress.currentPath)
                .font(.callout.monospaced())
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(maxWidth: 560)
            Button("Cancel", role: .cancel) { store.cancelScan() }
                .keyboardShortcut(.cancelAction)
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private func stat(_ value: String, _ caption: String) -> some View {
        VStack(spacing: 2) {
            Text(value).font(.system(size: 28, weight: .bold, design: .rounded).monospacedDigit())
            Text(caption).foregroundStyle(.secondary)
        }
    }
}

struct FailureView: View {
    @Environment(DiskStore.self) private var store
    let message: String

    var body: some View {
        ContentUnavailableView {
            Label("Scan Failed", systemImage: "exclamationmark.triangle")
        } description: {
            Text(message)
        } actions: {
            Button("Try Again") { store.rescan() }
        }
    }
}
