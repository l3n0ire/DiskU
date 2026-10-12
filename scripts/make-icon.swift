// Turns Resources/AppIcon.png into Resources/AppIcon.icns.
// The source art sits on an opaque white background, so this lifts the subject out with Vision,
// trims to it, and fits it inside the 824/1024 artwork area of Apple's macOS icon grid.
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
    let s = CGFloat(size), box = s * 824 / 1024
    let scale = box / CGFloat(max(subject.width, subject.height))
    let w = CGFloat(subject.width) * scale, h = CGFloat(subject.height) * scale
    ctx.draw(subject, in: CGRect(x: (s - w) / 2, y: (s - h) / 2, width: w, height: h))
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
