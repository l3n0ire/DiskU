import SwiftUI

public struct DiskUCommands: Commands {
    let store: DiskStore

    public init(store: DiskStore) { self.store = store }

    public var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Scan Folder…") { if let url = Actions.chooseFolder() { store.scan(url) } }
                .keyboardShortcut("o")
            Button("Scan Home Folder") { store.scan(FileManager.default.homeDirectoryForCurrentUser) }
                .keyboardShortcut("h", modifiers: [.command, .shift])
            Button("Rescan") { store.rescan() }
                .keyboardShortcut("r")
                .disabled(store.lastScanned == nil)
        }
        CommandMenu("Item") {
            let node = store.selection
            Button("Reveal in Finder") { node.map(Actions.reveal) }
                .keyboardShortcut("r", modifiers: [.command, .shift])
                .disabled(node == nil)
            Button("Quick Look") { node.map(Actions.quickLook) }
                .keyboardShortcut("y")
                .disabled(node == nil)
            Button("Open") { node.map(Actions.open) }
                .keyboardShortcut(.downArrow)
                .disabled(node == nil)
            Button("Zoom Out") { store.zoomRoot = store.zoomRoot?.parent === store.root ? nil : store.zoomRoot?.parent }
                .keyboardShortcut(.upArrow)
                .disabled(store.zoomRoot == nil)
            Divider()
            Button("Move to Trash…") { store.pendingTrash = node }
                .keyboardShortcut(.delete)
                .disabled(node == nil || node === store.root)
        }
    }
}
