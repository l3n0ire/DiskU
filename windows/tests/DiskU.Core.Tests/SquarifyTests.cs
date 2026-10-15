using DiskU.Core;

namespace DiskU.Core.Tests;

[TestClass]
public sealed class SquarifyTests
{
    private static void ExpectClose(RectD[] actual, RectD[] expected, double tolerance = 0.001)
    {
        Assert.HasCount(expected.Length, actual);
        for (int i = 0; i < expected.Length; i++)
        {
            var (a, e) = (actual[i], expected[i]);
            Assert.IsTrue(Math.Abs(a.MinX - e.MinX) < tolerance && Math.Abs(a.MinY - e.MinY) < tolerance
                && Math.Abs(a.Width - e.Width) < tolerance && Math.Abs(a.Height - e.Height) < tolerance,
                $"got {a}, expected {e}");
        }
    }

    [TestMethod]
    public void SquarifyMatchesBrulsPaperExample()
    {
        var rects = Squarify.Layout([6, 6, 4, 3, 2, 2, 1], new RectD(0, 0, 6, 4));
        ExpectClose(rects,
        [
            new(0, 0, 3, 2),
            new(0, 2, 3, 2),
            new(3, 0, 12.0 / 7.0, 7.0 / 3.0),
            new(3 + 12.0 / 7.0, 0, 9.0 / 7.0, 7.0 / 3.0),
            new(3, 7.0 / 3.0, 1.2, 5.0 / 3.0),
            new(4.2, 7.0 / 3.0, 1.2, 5.0 / 3.0),
            new(5.4, 7.0 / 3.0, 0.6, 5.0 / 3.0),
        ]);
    }

    [TestMethod]
    public void SquarifyScalesToRectAndOffset()
    {
        var rects = Squarify.Layout([1, 1], new RectD(10, 20, 100, 50));
        ExpectClose(rects, [new(10, 20, 50, 50), new(60, 20, 50, 50)]);
    }

    [TestMethod]
    public void SquarifyDegenerateInputsYieldZeroRects()
    {
        CollectionAssert.AreEqual(new[] { RectD.Zero, RectD.Zero }, Squarify.Layout([0, 0], new RectD(0, 0, 10, 10)));
        Assert.IsEmpty(Squarify.Layout([], new RectD(0, 0, 10, 10)));
    }

    // ---- Windows additions ----

    [TestMethod]
    public void RectContainsIsHalfOpen()
    {
        var r = new RectD(0, 0, 10, 10);
        Assert.IsTrue(r.Contains(0, 0));
        Assert.IsTrue(r.Contains(9.999, 9.999));
        Assert.IsFalse(r.Contains(10, 5));
        Assert.IsFalse(r.Contains(5, 10));
    }
}
