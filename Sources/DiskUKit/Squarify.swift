import CoreGraphics

/// Squarified treemap (Bruls, Huizing, van Wijk 2000).
/// `sizes` must be sorted descending; the result has one rect per size, in the same order,
/// and the rects tile `rect` exactly when all sizes are positive.
public func squarify(sizes: [Double], in rect: CGRect) -> [CGRect] {
    let total = sizes.reduce(0, +)
    guard total > 0, rect.width > 0, rect.height > 0 else {
        return Array(repeating: .zero, count: sizes.count)
    }
    let scale = Double(rect.width * rect.height) / total
    let areas = sizes.map { $0 * scale }

    var result: [CGRect] = []
    result.reserveCapacity(areas.count)
    var free = rect
    var start = 0

    while start < areas.count {
        let side = Double(min(free.width, free.height))
        var end = start + 1
        var rowSum = areas[start]
        var best = worstRatio(areas[start..<end], sum: rowSum, side: side)
        while end < areas.count {
            let candidateSum = rowSum + areas[end]
            let candidate = worstRatio(areas[start...end], sum: candidateSum, side: side)
            if candidate > best { break }
            best = candidate
            rowSum = candidateSum
            end += 1
        }
        free = layoutRow(areas[start..<end], sum: rowSum, in: free, into: &result)
        start = end
    }
    return result
}

private func worstRatio(_ row: ArraySlice<Double>, sum: Double, side: Double) -> Double {
    guard let maxArea = row.max(), let minArea = row.min(), sum > 0, minArea > 0 else {
        return .infinity
    }
    let s2 = side * side
    let sum2 = sum * sum
    return max(s2 * maxArea / sum2, sum2 / (s2 * minArea))
}

/// Lays the row along the shorter side of `free` and returns the remaining rect.
private func layoutRow(_ row: ArraySlice<Double>, sum: Double, in free: CGRect, into result: inout [CGRect]) -> CGRect {
    if free.width >= free.height {
        let columnWidth = CGFloat(sum / Double(free.height))
        var y = free.minY
        for area in row {
            let h = CGFloat(area) / columnWidth
            result.append(CGRect(x: free.minX, y: y, width: columnWidth, height: h))
            y += h
        }
        return CGRect(x: free.minX + columnWidth, y: free.minY,
                      width: max(0, free.width - columnWidth), height: free.height)
    } else {
        let rowHeight = CGFloat(sum / Double(free.width))
        var x = free.minX
        for area in row {
            let w = CGFloat(area) / rowHeight
            result.append(CGRect(x: x, y: free.minY, width: w, height: rowHeight))
            x += w
        }
        return CGRect(x: free.minX, y: free.minY + rowHeight,
                      width: free.width, height: max(0, free.height - rowHeight))
    }
}
