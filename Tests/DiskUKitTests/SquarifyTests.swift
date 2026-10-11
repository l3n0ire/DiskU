import CoreGraphics
import Testing
@testable import DiskUKit

private func expectClose(_ actual: [CGRect], _ expected: [CGRect], tolerance: CGFloat = 0.001) {
    #expect(actual.count == expected.count)
    for (a, e) in zip(actual, expected) {
        #expect(abs(a.minX - e.minX) < tolerance && abs(a.minY - e.minY) < tolerance
            && abs(a.width - e.width) < tolerance && abs(a.height - e.height) < tolerance,
            "got \(a), expected \(e)")
    }
}

@Test func squarifyMatchesBrulsPaperExample() {
    let rects = squarify(sizes: [6, 6, 4, 3, 2, 2, 1], in: CGRect(x: 0, y: 0, width: 6, height: 4))
    expectClose(rects, [
        CGRect(x: 0, y: 0, width: 3, height: 2),
        CGRect(x: 0, y: 2, width: 3, height: 2),
        CGRect(x: 3, y: 0, width: 12.0 / 7.0, height: 7.0 / 3.0),
        CGRect(x: 3 + 12.0 / 7.0, y: 0, width: 9.0 / 7.0, height: 7.0 / 3.0),
        CGRect(x: 3, y: 7.0 / 3.0, width: 1.2, height: 5.0 / 3.0),
        CGRect(x: 4.2, y: 7.0 / 3.0, width: 1.2, height: 5.0 / 3.0),
        CGRect(x: 5.4, y: 7.0 / 3.0, width: 0.6, height: 5.0 / 3.0),
    ])
}

@Test func squarifyScalesToRectAndOffset() {
    let rects = squarify(sizes: [1, 1], in: CGRect(x: 10, y: 20, width: 100, height: 50))
    expectClose(rects, [
        CGRect(x: 10, y: 20, width: 50, height: 50),
        CGRect(x: 60, y: 20, width: 50, height: 50),
    ])
}

@Test func squarifyDegenerateInputsYieldZeroRects() {
    #expect(squarify(sizes: [0, 0], in: CGRect(x: 0, y: 0, width: 10, height: 10)) == [.zero, .zero])
    #expect(squarify(sizes: [], in: CGRect(x: 0, y: 0, width: 10, height: 10)) == [])
}
