import SwiftUI

public enum Kind: String, CaseIterable, Sendable {
    case folder, bundle, image, video, audio, archive, code, document, other

    public static func forFile(named name: String) -> Kind {
        let ext = (name as NSString).pathExtension.lowercased()
        return extensionTable[ext] ?? .other
    }

    public var label: String { style.label }
    public var color: Color { style.color }

    private var style: (label: String, color: Color) {
        switch self {
        case .folder: return ("Folders", Color(red: 0.42, green: 0.45, blue: 0.50))
        case .bundle: return ("Apps & Bundles", Color(red: 0.62, green: 0.40, blue: 0.86))
        case .image: return ("Images", Color(red: 0.20, green: 0.70, blue: 0.45))
        case .video: return ("Video", Color(red: 0.90, green: 0.30, blue: 0.30))
        case .audio: return ("Audio", Color(red: 0.95, green: 0.60, blue: 0.20))
        case .archive: return ("Archives & Disk Images", Color(red: 0.85, green: 0.75, blue: 0.25))
        case .code: return ("Code & Data", Color(red: 0.25, green: 0.65, blue: 0.85))
        case .document: return ("Documents", Color(red: 0.30, green: 0.45, blue: 0.90))
        case .other: return ("System & Other", Color(red: 0.55, green: 0.58, blue: 0.62))
        }
    }

    private static let extensionTable: [String: Kind] = {
        let groups: [Kind: [String]] = [
            .image: ["jpg", "jpeg", "png", "gif", "heic", "heif", "tif", "tiff", "bmp", "webp", "raw", "cr2", "nef", "arw", "dng", "psd", "svg", "ico", "icns"],
            .video: ["mov", "mp4", "m4v", "avi", "mkv", "wmv", "flv", "webm", "mpg", "mpeg", "3gp"],
            .audio: ["mp3", "m4a", "aac", "wav", "aiff", "aif", "flac", "ogg", "caf", "alac"],
            .archive: ["zip", "gz", "tgz", "bz2", "xz", "7z", "rar", "tar", "dmg", "iso", "pkg", "sparseimage", "sparsebundle", "zst", "xip"],
            .code: ["swift", "m", "h", "c", "cc", "cpp", "hpp", "rs", "go", "py", "js", "mjs", "ts", "tsx", "jsx", "java", "kt", "rb", "php", "sh", "json", "yaml", "yml", "toml", "xml", "plist", "sql", "sqlite", "db", "html", "css", "o", "a", "dylib", "so", "wasm", "jar", "class", "pyc"],
            .document: ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "pages", "numbers", "key", "txt", "md", "rtf", "csv", "epub", "odt"],
        ]
        var table: [String: Kind] = [:]
        for (kind, exts) in groups { for ext in exts { table[ext] = kind } }
        return table
    }()
}
