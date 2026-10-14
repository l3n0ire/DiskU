import Foundation
import Testing
@testable import DiskUKit

@Test func versionsCompareNumberByNumber() throws {
    let v = { (s: String) in try #require(Version(s)) }
    #expect(try v("v1.0.10") > v("1.0.9"))
    #expect(try v("v1.1") > v("v1.0.3"))
    #expect(try v("1.0") == v("v1.0.0"))
    #expect(try v("v2.0.0").description == "2.0.0")
    #expect(Version("v1.0-beta") == nil)
    #expect(Version("latest") == nil)
}

@Test func releaseDecodesFromGitHubResponse() throws {
    let json = Data(#"{"tag_name": "v1.0.4", "html_url": "https://github.com/l3n0ire/DiskU/releases/tag/v1.0.4", "draft": false}"#.utf8)
    let release = try JSONDecoder().decode(Release.self, from: json)
    #expect(release.version.description == "1.0.4")
    #expect(release.url.absoluteString == "https://github.com/l3n0ire/DiskU/releases/tag/v1.0.4")
    #expect(throws: DecodingError.self) {
        try JSONDecoder().decode(Release.self, from: Data(#"{"tag_name": "nightly", "html_url": "https://x"}"#.utf8))
    }
}
