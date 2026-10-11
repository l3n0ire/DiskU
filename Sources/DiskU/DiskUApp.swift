import DiskUKit
import SwiftUI

@main
struct DiskUApp: App {
    @NSApplicationDelegateAdaptor(DiskUAppDelegate.self) private var delegate
    @State private var store = DiskStore()

    var body: some Scene {
        WindowGroup("DiskU") {
            ContentView()
                .environment(store)
        }
        .defaultSize(width: 1200, height: 800)
        .commands { DiskUCommands(store: store) }
    }
}
