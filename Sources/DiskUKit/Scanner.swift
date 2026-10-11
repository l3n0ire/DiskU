import Foundation
import os

public struct ScanProgress: Sendable, Equatable {
    public var filesSeen = 0
    public var bytesSeen: Int64 = 0
    public var currentPath = ""
    public init() {}
}

public enum ScanState {
    case idle
    case scanning(progress: ScanProgress)
    case done(root: FileNode, inaccessible: Int)
    case failed(String)
}

public struct ScanResult: Sendable {
    public let root: FileNode
    public let inaccessible: Int
    public let elapsed: Duration
}

public enum Scanner {
    /// Scans `url` without following symlinks or crossing onto other volumes.
    /// `onProgress` is called on the main actor at most ~10 times per second, and never after
    /// this function returns or throws.
    public static func scan(
        _ url: URL,
        onProgress: @escaping @MainActor @Sendable (ScanProgress) -> Void = { _ in }
    ) async throws -> ScanResult {
        let start = ContinuousClock.now
        let walker = try Walker(root: url.standardizedFileURL)

        let ticker = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .milliseconds(100))
                await onProgress(walker.progress)
            }
        }
        let root: FileNode
        do {
            root = try await walker.scanParallel(walker.root, depth: 0)
        } catch {
            ticker.cancel()
            await ticker.value
            throw error
        }
        // Drain the ticker so no progress callback can land after the caller sees the result.
        ticker.cancel()
        await ticker.value
        await onProgress(walker.progress)
        return ScanResult(root: root, inaccessible: walker.inaccessible, elapsed: ContinuousClock.now - start)
    }
}

private final class Walker: @unchecked Sendable {
    /// Directories this shallow get their subdirectories scanned concurrently; deeper ones
    /// run synchronously inside their ancestor's task, which bounds the number of tasks.
    static let parallelDepth = 3

    static let keys: [URLResourceKey] = [
        .isDirectoryKey, .isSymbolicLinkKey, .isPackageKey,
        .totalFileAllocatedSizeKey, .fileAllocatedSizeKey, .volumeIdentifierKey,
    ]
    static let keySet = Set(keys)

    let root: URL
    private let allowedVolumes: [NSObject]
    private let skippedPaths: Set<String>
    private let counters = OSAllocatedUnfairLock(initialState: (progress: ScanProgress(), inaccessible: 0))

    init(root: URL) throws {
        let values = try root.resourceValues(forKeys: [.isDirectoryKey, .volumeIdentifierKey])
        guard values.isDirectory == true else {
            throw CocoaError(.fileReadInvalidFileName, userInfo: [NSFilePathErrorKey: root.path])
        }
        self.root = root
        var volumes = [values.volumeIdentifier as? NSObject].compactMap { $0 }
        if root.path == "/" {
            // "/" is the sealed system volume; user data lives on the Data volume and is
            // reachable through firmlinks, so allow it but skip its second mount point.
            // /.nofollow and /.resolve are path-resolution aliases that re-list the whole root.
            let data = URL(fileURLWithPath: "/System/Volumes/Data")
            if let id = try? data.resourceValues(forKeys: [.volumeIdentifierKey]).volumeIdentifier as? NSObject {
                volumes.append(id)
            }
            skippedPaths = ["/System/Volumes", "/Volumes", "/.nofollow", "/.resolve", "/.vol", "/dev"]
        } else {
            skippedPaths = []
        }
        allowedVolumes = volumes
    }

    var progress: ScanProgress { counters.withLock { $0.progress } }
    var inaccessible: Int { counters.withLock { $0.inaccessible } }

    func scanParallel(_ url: URL, depth: Int) async throws -> FileNode {
        if depth >= Self.parallelDepth { return try scanSync(url) }
        try Task.checkCancellation()
        let (leaves, subdirectories) = entries(of: url)
        let children = try await withThrowingTaskGroup(of: FileNode.self) { group in
            for dir in subdirectories {
                group.addTask { try await self.scanParallel(dir, depth: depth + 1) }
            }
            var nodes = leaves
            for try await node in group { nodes.append(node) }
            return nodes
        }
        let node = FileNode(url: url, kind: .folder)
        node.adopt(children)
        return node
    }

    private func scanSync(_ url: URL) throws -> FileNode {
        try Task.checkCancellation()
        var (children, subdirectories) = entries(of: url)
        for dir in subdirectories { children.append(try scanSync(dir)) }
        let node = FileNode(url: url, kind: .folder)
        node.adopt(children)
        return node
    }

    /// Lists one directory: files, symlinks and bundles become leaves; plain directories on an
    /// allowed volume are returned for the caller to descend into.
    private func entries(of url: URL) -> (leaves: [FileNode], subdirectories: [URL]) {
        let contents: [URL]
        do {
            contents = try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: Self.keys)
        } catch {
            counters.withLock { $0.inaccessible += 1 }
            return ([], [])
        }

        var leaves: [FileNode] = []
        var subdirectories: [URL] = []
        var bytes: Int64 = 0
        var files = 0
        for child in contents {
            guard let values = try? child.resourceValues(forKeys: Self.keySet) else { continue }
            if values.isSymbolicLink == true {
                leaves.append(FileNode(url: child, kind: .other, size: Self.allocated(values), itemCount: 1))
                files += 1
            } else if values.isDirectory == true {
                if !skippedPaths.isEmpty, skippedPaths.contains(Self.trimmedPath(child)) { continue }
                guard isAllowedVolume(values.volumeIdentifier) else { continue }
                if values.isPackage == true {
                    let (size, count) = packageTotals(child)
                    leaves.append(FileNode(url: child, kind: .bundle, size: size, itemCount: count))
                    bytes += size
                    files += count
                } else {
                    subdirectories.append(child)
                }
            } else {
                let size = Self.allocated(values)
                leaves.append(FileNode(url: child, kind: Kind.forFile(named: child.lastPathComponent), size: size, itemCount: 1))
                bytes += size
                files += 1
            }
        }
        counters.withLock { [files, bytes] in
            $0.progress.filesSeen += files
            $0.progress.bytesSeen += bytes
            $0.progress.currentPath = url.path
        }
        return (leaves, subdirectories)
    }

    /// Bundles are sized like directories but kept as one opaque leaf in the tree.
    private func packageTotals(_ url: URL) -> (Int64, Int) {
        guard let enumerator = FileManager.default.enumerator(
            at: url, includingPropertiesForKeys: Self.keys, options: [], errorHandler: { _, _ in true }
        ) else { return (0, 0) }
        var size: Int64 = 0
        var count = 0
        for case let file as URL in enumerator {
            guard let values = try? file.resourceValues(forKeys: Self.keySet), values.isDirectory != true else { continue }
            size += Self.allocated(values)
            count += 1
        }
        return (size, count)
    }

    private func isAllowedVolume(_ id: (any NSCopying & NSSecureCoding & NSObjectProtocol)?) -> Bool {
        guard let id = id as? NSObject else { return true }
        return allowedVolumes.contains { $0.isEqual(id) }
    }

    /// Foundation reports some root entries (e.g. "/.nofollow/") with a trailing slash.
    private static func trimmedPath(_ url: URL) -> String {
        let path = url.path
        return path.count > 1 && path.hasSuffix("/") ? String(path.dropLast()) : path
    }

    private static func allocated(_ values: URLResourceValues) -> Int64 {
        Int64(values.totalFileAllocatedSize ?? values.fileAllocatedSize ?? 0)
    }
}
