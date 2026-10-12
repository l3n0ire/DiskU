import AppKit
import DiskUKit
import SwiftUI

/// diskusnap <dir> <out.png> [--renderer window|image] [--zoom]
/// diskusnap --home <out.png>
/// Scans <dir> with the real scanner, selects its largest child, and renders the real browser view.
/// With --home it skips scanning and renders the home screen instead.
@main
struct DiskUSnap {
    static let size = CGSize(width: 1400, height: 900)

    @MainActor
    static func main() async {
        var args = Array(CommandLine.arguments.dropFirst())
        if args.count == 2, args[0] == "--home" {
            let view = ContentView().environment(DiskStore())
                .frame(width: size.width, height: size.height)
                .background(Color(white: 0.12))
            guard let png = renderInWindow(view)?.representation(using: .png, properties: [:]) else { exit(1) }
            try! png.write(to: URL(fileURLWithPath: args[1]))
            print("wrote \(args[1]) (Full Disk Access: \(Actions.hasFullDiskAccess ? "on" : "off"))")
            return
        }
        let zoom = args.contains("--zoom")
        args.removeAll { $0 == "--zoom" }
        var renderer = "window"
        if let index = args.firstIndex(of: "--renderer"), index + 1 < args.count {
            renderer = args[index + 1]
            args.removeSubrange(index...index + 1)
        }
        guard args.count == 2 else {
            FileHandle.standardError.write(Data("usage: diskusnap <dir> <out.png> [--renderer window|image] [--zoom]\n       diskusnap --home <out.png>\n".utf8))
            exit(64)
        }
        let dir = URL(fileURLWithPath: (args[0] as NSString).expandingTildeInPath)
        let out = URL(fileURLWithPath: args[1])

        let result: ScanResult
        do {
            result = try await Scanner.scan(dir)
        } catch {
            FileHandle.standardError.write(Data("scan failed: \(error.localizedDescription)\n".utf8))
            exit(1)
        }
        let seconds = Double(result.elapsed.components.seconds) + Double(result.elapsed.components.attoseconds) / 1e18
        print(String(format: "scanned %@ in %.2fs: %d files, %lld bytes (%@), %d inaccessible",
                     dir.path, seconds, result.root.itemCount, result.root.size,
                     Format.bytes(result.root.size), result.inaccessible))

        let store = DiskStore()
        store.finish(result)
        let largest = result.root.children.first
        if zoom, let largest, largest.isFolder {
            store.zoom(into: largest)
            store.selection = largest.children.first
        } else {
            store.selection = largest
        }
        if let selection = store.selection {
            print("selected \(selection.name) (\(Format.bytes(selection.size)))")
        }

        let view = BrowserView(root: result.root, inaccessible: result.inaccessible)
            .environment(store)
            .frame(width: size.width, height: size.height)

        let image: NSBitmapImageRep?
        switch renderer {
        case "image":
            // ImageRenderer cannot draw NavigationSplitView or List, so this mode renders
            // only the pure-SwiftUI treemap pane.
            print("note: image renderer draws the treemap pane only (no outline or inspector)")
            image = renderWithImageRenderer(
                TreemapPane(inaccessible: result.inaccessible)
                    .environment(store)
                    .environment(\.colorScheme, .dark)
                    .frame(width: size.width, height: size.height)
                    .background(Color(white: 0.12))
            )
        default: image = renderInWindow(view)
        }
        guard let image, let png = image.representation(using: .png, properties: [:]) else {
            FileHandle.standardError.write(Data("render failed\n".utf8))
            exit(1)
        }
        do {
            try png.write(to: out)
        } catch {
            FileHandle.standardError.write(Data("write failed: \(error.localizedDescription)\n".utf8))
            exit(1)
        }
        print("wrote \(out.path) via \(renderer) renderer")
    }

    @MainActor
    static func renderWithImageRenderer(_ view: some View) -> NSBitmapImageRep? {
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        guard let cgImage = renderer.cgImage else { return nil }
        return NSBitmapImageRep(cgImage: cgImage)
    }

    /// Hosts the view in an offscreen window so AppKit-backed views (List, split view) render.
    /// Known gap: system-drawn button bezels and their labels come out blank in this capture.
    @MainActor
    static func renderInWindow(_ view: some View) -> NSBitmapImageRep? {
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        let window = NSWindow(contentRect: CGRect(origin: CGPoint(x: -10000, y: -10000), size: size),
                              styleMask: [.titled], backing: .buffered, defer: false)
        window.appearance = NSAppearance(named: .darkAqua)
        let host = NSHostingView(rootView: view)
        window.contentView = host
        window.orderFrontRegardless()
        // Let List populate rows and async Canvas layers finish drawing.
        for _ in 0..<20 {
            RunLoop.main.run(until: Date().addingTimeInterval(0.05))
            host.layoutSubtreeIfNeeded()
        }
        guard let rep = host.bitmapImageRepForCachingDisplay(in: host.bounds) else { return nil }
        window.appearance?.performAsCurrentDrawingAppearance {
            host.cacheDisplay(in: host.bounds, to: rep)
        }
        window.orderOut(nil)
        return rep
    }
}
