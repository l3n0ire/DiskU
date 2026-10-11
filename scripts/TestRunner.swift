import Foundation
import Testing

@main struct TestRunner {
    static func main() async {
        let code: CInt = await Testing.__swiftPMEntryPoint()
        exit(code)
    }
}
