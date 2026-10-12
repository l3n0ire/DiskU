import AppKit
import Quartz
import SwiftUI

public enum Format {
    private static let byteFormatter: ByteCountFormatter = {
        let formatter = ByteCountFormatter()
        formatter.countStyle = .file
        formatter.allowsNonnumericFormatting = false
        return formatter
    }()

    public static func bytes(_ count: Int64) -> String {
        byteFormatter.string(fromByteCount: count)
    }

    public static func items(_ count: Int) -> String {
        count == 1 ? "1 file" : "\(count.formatted()) files"
    }
}

@MainActor
enum Icons {
    private static let cache = NSCache<NSString, NSImage>()

    static func icon(for url: URL) -> NSImage {
        if let cached = cache.object(forKey: url.path as NSString) { return cached }
        let image = NSWorkspace.shared.icon(forFile: url.path)
        cache.setObject(image, forKey: url.path as NSString)
        return image
    }
}

@MainActor
public enum Actions {
    public static func reveal(_ node: FileNode) {
        NSWorkspace.shared.activateFileViewerSelecting([node.url])
    }

    public static func open(_ node: FileNode) {
        NSWorkspace.shared.open(node.url)
    }

    public static func quickLook(_ node: FileNode) {
        QuickLookController.shared.toggle(node.url)
    }

    public static func openFullDiskAccessSettings() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles")!)
    }

    /// The Full Disk Access list has no way to add an app from code, so show the bundle for dragging in.
    public static func revealAppBundle() {
        NSWorkspace.shared.activateFileViewerSelecting([Bundle.main.bundleURL])
    }

    /// macOS has no API to ask whether Full Disk Access is granted. The TCC database is readable only
    /// with it, so opening the file is the standard probe.
    public static var hasFullDiskAccess: Bool {
        FileHandle(forReadingAtPath: "/Library/Application Support/com.apple.TCC/TCC.db") != nil
    }

    public static func chooseFolder() -> URL? {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.treatsFilePackagesAsDirectories = true
        panel.directoryURL = FileManager.default.homeDirectoryForCurrentUser
        panel.prompt = "Scan"
        panel.message = "Choose a folder to scan"
        return panel.runModal() == .OK ? panel.url : nil
    }
}

/// QLPreviewPanel only accepts a data source from an object in the responder chain, so the
/// app delegate forwards control here.
@MainActor
public final class QuickLookController: NSObject, QLPreviewPanelDataSource {
    public static let shared = QuickLookController()
    private var url: URL?

    func toggle(_ url: URL) {
        guard let panel = QLPreviewPanel.shared() else {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/qlmanage")
            process.arguments = ["-p", url.path]
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            try? process.run()
            return
        }
        if panel.isVisible, self.url == url {
            panel.orderOut(nil)
            return
        }
        self.url = url
        panel.reloadData()
        panel.makeKeyAndOrderFront(nil)
    }

    public func attach(_ panel: QLPreviewPanel) {
        panel.dataSource = self
        panel.reloadData()
    }

    public nonisolated func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int {
        MainActor.assumeIsolated { url == nil ? 0 : 1 }
    }

    public nonisolated func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> (any QLPreviewItem)! {
        MainActor.assumeIsolated { url as NSURL? }
    }
}

public final class DiskUAppDelegate: NSObject, NSApplicationDelegate {
    public func applicationDidFinishLaunching(_ notification: Notification) {
        // Running the bare executable (not the .app) would otherwise leave no Dock icon or menu bar.
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
    }

    public func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    public override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }

    public override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        MainActor.assumeIsolated { QuickLookController.shared.attach(panel) }
    }

    public override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {}
}
