namespace DiskU.Core;

/// <summary>A point in DIPs, y down.</summary>
public readonly record struct PointD(double X, double Y);

/// <summary>A rectangle in DIPs (origin top-left, y down), mirroring CGRect for the layout code.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public static RectD Zero => default;

    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    /// <summary>Same as CGRect.contains: minX &lt;= x &lt; maxX and minY &lt;= y &lt; maxY.</summary>
    public bool Contains(PointD p) => p.X >= MinX && p.X < MaxX && p.Y >= MinY && p.Y < MaxY;

    public bool Contains(double x, double y) => Contains(new PointD(x, y));

    /// <summary>Shrinks by dx on the left and right and dy on the top and bottom.</summary>
    public RectD Inset(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);

    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
}
