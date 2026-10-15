namespace DiskU.Core;

/// <summary>Squarified treemap (Bruls, Huizing, van Wijk 2000). Exact port of the Mac's squarify().</summary>
public static class Squarify
{
    /// <summary>
    /// Lays out <paramref name="sizes"/> (sorted descending) in <paramref name="rect"/>. Returns one rect per
    /// size in the same order; they tile the rect exactly when every size is positive.
    /// </summary>
    public static RectD[] Layout(IReadOnlyList<double> sizes, RectD rect)
    {
        var result = new RectD[sizes.Count];
        double total = 0;
        for (int i = 0; i < sizes.Count; i++) total += sizes[i];
        if (total <= 0 || rect.Width <= 0 || rect.Height <= 0) return result; // all zero rects

        double scale = rect.Width * rect.Height / total;
        var areas = new double[sizes.Count];
        for (int i = 0; i < sizes.Count; i++) areas[i] = sizes[i] * scale;

        int written = 0;
        var free = rect;
        int start = 0;
        while (start < areas.Length)
        {
            double side = Math.Min(free.Width, free.Height);
            int end = start + 1;
            double rowSum = areas[start];
            double best = Worst(areas, start, end, rowSum, side);
            while (end < areas.Length)
            {
                double candidateSum = rowSum + areas[end];
                double candidate = Worst(areas, start, end + 1, candidateSum, side);
                if (candidate > best) break; // an equal ratio keeps adding
                best = candidate;
                rowSum = candidateSum;
                end++;
            }
            free = LayoutRow(areas, start, end, rowSum, free, result, ref written);
            start = end;
        }
        return result;
    }

    /// <summary>Worst aspect ratio of areas[start..end) laid along a side of length <paramref name="side"/>.</summary>
    private static double Worst(double[] areas, int start, int end, double sum, double side)
    {
        if (end <= start || sum <= 0) return double.PositiveInfinity;
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        for (int i = start; i < end; i++)
        {
            min = Math.Min(min, areas[i]);
            max = Math.Max(max, areas[i]);
        }
        if (min <= 0) return double.PositiveInfinity;
        double s2 = side * side, sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }

    private static RectD LayoutRow(double[] areas, int start, int end, double sum, RectD free, RectD[] result, ref int written)
    {
        if (free.Width >= free.Height)
        {
            // A column on the left, filled top to bottom.
            double columnWidth = sum / free.Height;
            double y = free.MinY;
            for (int i = start; i < end; i++)
            {
                double h = areas[i] / columnWidth;
                result[written++] = new RectD(free.MinX, y, columnWidth, h);
                y += h;
            }
            return new RectD(free.MinX + columnWidth, free.MinY, Math.Max(0, free.Width - columnWidth), free.Height);
        }
        else
        {
            // A row on top, filled left to right.
            double rowHeight = sum / free.Width;
            double x = free.MinX;
            for (int i = start; i < end; i++)
            {
                double w = areas[i] / rowHeight;
                result[written++] = new RectD(x, free.MinY, w, rowHeight);
                x += w;
            }
            return new RectD(free.MinX, free.MinY + rowHeight, free.Width, Math.Max(0, free.Height - rowHeight));
        }
    }
}
