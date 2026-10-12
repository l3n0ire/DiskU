// Turns Resources/AppIcon.png into Resources/AppIcon.icns.
// The source art is an opaque square with black corners, so this crops to the bright tile, masks its
// corners transparent, and centers it at 824/1024 per Apple's macOS icon grid.
import AppKit

let root = URL(fileURLWithPath: CommandLine.arguments[1])
let source = NSImage(contentsOf: root.appendingPathComponent("Resources/AppIcon.png"))!
var rect = CGRect(origin: .zero, size: source.size)
let cg = source.cgImage(forProposedRect: &rect, context: nil, hints: nil)!
let bitmap = NSBitmapImageRep(cgImage: cg)

func isBright(_ x: Int, _ y: Int) -> Bool {
    guard let c = bitmap.colorAt(x: x, y: y) else { return false }
    return c.redComponent + c.greenComponent + c.blueComponent > 0.6
}
let w = bitmap.pixelsWide, h = bitmap.pixelsHigh, mid = h / 2
let left = (0..<w).first { isBright($0, mid) }!
let right = (0..<w).last { isBright($0, mid) }!
let top = (0..<h).first { isBright(w / 2, $0) }!
let bottom = (0..<h).last { isBright(w / 2, $0) }!
let tile = cg.cropping(to: CGRect(x: left, y: top, width: right - left + 1, height: bottom - top + 1))!

func render(_ size: Int) -> Data {
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    let s = CGFloat(size), side = s * 824 / 1024
    let frame = CGRect(x: (s - side) / 2, y: (s - side) / 2, width: side, height: side)
    ctx.addPath(CGPath(roundedRect: frame, cornerWidth: side * 0.225, cornerHeight: side * 0.225, transform: nil))
    ctx.clip()
    ctx.draw(tile, in: frame)
    return NSBitmapImageRep(cgImage: ctx.makeImage()!).representation(using: .png, properties: [:])!
}

let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("AppIcon.iconset")
try? FileManager.default.removeItem(at: iconset)
try! FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for base in [16, 32, 128, 256, 512] {
    try! render(base).write(to: iconset.appendingPathComponent("icon_\(base)x\(base).png"))
    try! render(base * 2).write(to: iconset.appendingPathComponent("icon_\(base)x\(base)@2x.png"))
}
let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path, "-o", root.appendingPathComponent("Resources/AppIcon.icns").path]
try! iconutil.run()
iconutil.waitUntilExit()
print("crop \(left),\(top) to \(right),\(bottom) of \(w)x\(h)")
exit(iconutil.terminationStatus)
