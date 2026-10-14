import DiskUKit
import SwiftUI

@main
struct DiskUApp: App {
    @NSApplicationDelegateAdaptor(DiskUAppDelegate.self) private var delegate
    @State private var store = DiskStore()
    @State private var updates = UpdateChecker()

    var body: some Scene {
        WindowGroup("DiskU") {
            ContentView()
                .environment(store)
                .environment(updates)
                .task { if let folder = Self.launchFolder { store.scan(folder) } }
                .task { await updates.check() }
        }
        .defaultSize(width: 1200, height: 800)
        .commands { DiskUCommands(store: store) }
    }

    /// A folder passed at launch (`open DiskU.app --args ~/Downloads`) is scanned right away.
    private static var launchFolder: URL? {
        CommandLine.arguments.dropFirst().lazy
            .filter { !$0.hasPrefix("-") }
            .map { URL(fileURLWithPath: ($0 as NSString).expandingTildeInPath) }
            .first { (try? $0.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true }
    }
}
