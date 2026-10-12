// Turns Resources/AppIcon.png into Resources/AppIcon.icns.
// The source art sits on an opaque white background, so this lifts the subject out with Vision and
// places it on a blue rounded tile filling Apple's 824/1024 icon grid. macOS 26 puts any icon that
// is not a full rounded square on a gray tile, so the art must supply its own.
import AppKit
import CoreImage
import Vision

let root = URL(fileURLWithPath: CommandLine.arguments[1])
let source = CIImage(contentsOf: root.appendingPathComponent("Resources/AppIcon.png"))!

let request = VNGenerateForegroundInstanceMaskRequest()
let handler = VNImageRequestHandler(ciImage: source)
try! handler.perform([request])
let observation = request.results!.first!
let cutout = CIImage(cvPixelBuffer: try! observation.generateMaskedImage(
    ofInstances: observation.allInstances, from: handler, croppedToInstancesExtent: true))

let context = CIContext()
let subject = context.createCGImage(cutout, from: cutout.extent)!

func render(_ size: Int) -> Data {
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.interpolationQuality = .high
    let s = CGFloat(size), side = s * 824 / 1024
    let tile = CGRect(x: (s - side) / 2, y: (s - side) / 2, width: side, height: side)

    ctx.saveGState()
    ctx.addPath(CGPath(roundedRect: tile, cornerWidth: side * 0.225, cornerHeight: side * 0.225, transform: nil))
    ctx.clip()
    let gradient = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB)!, colors: [
        CGColor(srgbRed: 0.55, green: 0.80, blue: 1.00, alpha: 1),
        CGColor(srgbRed: 0.15, green: 0.45, blue: 0.95, alpha: 1),
    ] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: tile.maxY), end: CGPoint(x: 0, y: tile.minY), options: [])
    ctx.restoreGState()

    let scale = side * 0.88 / CGFloat(max(subject.width, subject.height))
    let w = CGFloat(subject.width) * scale, h = CGFloat(subject.height) * scale
    ctx.draw(subject, in: CGRect(x: tile.midX - w / 2, y: tile.midY - h / 2, width: w, height: h))
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
print("subject \(subject.width)x\(subject.height)")
exit(iconutil.terminationStatus)
