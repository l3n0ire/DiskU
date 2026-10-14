import Foundation
import Observation

/// A release version such as `v1.2.3`, compared number by number so 1.0.10 is newer than 1.0.9.
public struct Version: Comparable, CustomStringConvertible, Sendable {
    public let parts: [Int]

    public init?(_ string: String) {
        let trimmed = string.hasPrefix("v") ? String(string.dropFirst()) : string
        let parts = trimmed.split(separator: ".").map { Int($0) }
        guard !parts.isEmpty, !parts.contains(nil) else { return nil }
        self.parts = parts.compactMap { $0 }
    }

    public static func < (a: Version, b: Version) -> Bool {
        for i in 0..<max(a.parts.count, b.parts.count) {
            let x = i < a.parts.count ? a.parts[i] : 0
            let y = i < b.parts.count ? b.parts[i] : 0
            if x != y { return x < y }
        }
        return false
    }

    /// Equal when neither is newer, so 1.0 == 1.0.0.
    public static func == (a: Version, b: Version) -> Bool { !(a < b) && !(b < a) }

    public var description: String { parts.map(String.init).joined(separator: ".") }
}

public struct Release: Decodable, Sendable {
    public let version: Version
    public let url: URL

    private enum CodingKeys: String, CodingKey { case tagName = "tag_name", htmlURL = "html_url" }

    public init(version: Version, url: URL) {
        self.version = version
        self.url = url
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let tag = try container.decode(String.self, forKey: .tagName)
        guard let version = Version(tag) else {
            throw DecodingError.dataCorruptedError(forKey: .tagName, in: container, debugDescription: "not a version: \(tag)")
        }
        self.version = version
        self.url = try container.decode(URL.self, forKey: .htmlURL)
    }
}

/// Looks up the latest GitHub release once and remembers it if it is newer than the running app.
@MainActor @Observable
public final class UpdateChecker {
    public static let latestReleaseURL = URL(string: "https://api.github.com/repos/l3n0ire/DiskU/releases/latest")!

    public private(set) var available: Release?
    public var dismissed = false

    public init() {}

    /// The running app's version, or nil when not launched from a bundle (e.g. `swift run`).
    nonisolated public static var currentVersion: Version? {
        (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String).flatMap(Version.init)
    }

    /// Silent on any failure: being offline or rate limited must never bother the user.
    public func check(current: Version? = UpdateChecker.currentVersion) async {
        guard let current else { return }
        var request = URLRequest(url: Self.latestReleaseURL, timeoutInterval: 10)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        guard let (data, response) = try? await URLSession.shared.data(for: request),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let latest = try? JSONDecoder().decode(Release.self, from: data)
        else { return }
        available = current < latest.version ? latest : nil
    }
}
