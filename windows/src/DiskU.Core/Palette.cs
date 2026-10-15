namespace DiskU.Core;

/// <summary>Treemap colors, copied from the Mac's <c>Palette</c> (mac-spec section 2.9).</summary>
public static class Palette
{
    /// <summary>Canvas background, white 0.08.</summary>
    public static Rgb Background { get; } = Rgb.Gray(0x14);

    /// <summary>Leaf folder with no children or zero size, white 0.35.</summary>
    public static Rgb EmptyFolder { get; } = Rgb.Gray(0x59);

    /// <summary>Tile that aggregates a folder's sub-pixel children, white 0.30.</summary>
    public static Rgb Remainder { get; } = Rgb.Gray(0x4D);

    // white max(0.10, 0.20 - depth*0.02) and max(0.16, 0.28 - depth*0.025), rounded to bytes.
    private static readonly byte[] FolderFills = [0x33, 0x2E, 0x29, 0x24, 0x1F, 0x1A];
    private static readonly byte[] HeaderFills = [0x47, 0x41, 0x3B, 0x34, 0x2E, 0x29];

    public static Rgb FolderFill(int depth) => Rgb.Gray(FolderFills[Math.Clamp(depth, 0, FolderFills.Length - 1)]);

    public static Rgb HeaderFill(int depth) => Rgb.Gray(HeaderFills[Math.Clamp(depth, 0, HeaderFills.Length - 1)]);

    /// <summary>Gradient stop positions of the cushion: highlight, base, shadow.</summary>
    public static IReadOnlyList<double> CushionStops { get; } = [0.0, 0.45, 1.0];

    /// <summary>
    /// Cushion colors for a leaf of <paramref name="kind"/> (null = empty folder): base blended 35% toward
    /// white at 0, the base at 0.45, base blended 45% toward black at 1. The gradient runs in absolute
    /// coordinates from the tile's top-left corner to its bottom-right corner.
    /// </summary>
    public static (Rgb Highlight, Rgb Base, Rgb Shadow) Cushion(Kind? kind) => kind switch
    {
        null => (Rgb.FromHex(0x939393), Rgb.FromHex(0x595959), Rgb.FromHex(0x313131)),
        Kind.Folder => (Rgb.FromHex(0x9FA4AC), Rgb.FromHex(0x6B7380), Rgb.FromHex(0x3B3F46)),
        Kind.Bundle => (Rgb.FromHex(0xC09CE8), Rgb.FromHex(0x9E66DB), Rgb.FromHex(0x573879)),
        Kind.Image => (Rgb.FromHex(0x7ACDA4), Rgb.FromHex(0x33B373), Rgb.FromHex(0x1C623F)),
        Kind.Video => (Rgb.FromHex(0xEE8B8B), Rgb.FromHex(0xE64D4D), Rgb.FromHex(0x7E2A2A)),
        Kind.Audio => (Rgb.FromHex(0xF7BD7A), Rgb.FromHex(0xF29933), Rgb.FromHex(0x85541C)),
        Kind.Archive => (Rgb.FromHex(0xE6D683), Rgb.FromHex(0xD9BF40), Rgb.FromHex(0x776923)),
        Kind.Code => (Rgb.FromHex(0x83C5E6), Rgb.FromHex(0x40A6D9), Rgb.FromHex(0x235B77)),
        Kind.Document => (Rgb.FromHex(0x8BA4EE), Rgb.FromHex(0x4D73E6), Rgb.FromHex(0x2A3F7E)),
        _ => (Rgb.FromHex(0xB4B9C0), Rgb.FromHex(0x8C949E), Rgb.FromHex(0x4D5157)),
    };

    /// <summary>
    /// Kind used to color a leaf tile: the node's kind, except folders (empty or zero-size) which
    /// use the empty-folder colors (null).
    /// </summary>
    public static Kind? LeafKind(FileNode node) => node.IsFolder ? null : node.Kind;

    // Treemap drawing constants (points/DIPs).
    public const double LeafStrokeInset = 0.25;
    public const double LeafStrokeWidth = 0.5;
    public const double LeafStrokeOpacity = 0.35;
    public const double SolidFillBelow = 3;
    public const double LeafLabelMinWidth = 56;
    public const double LeafLabelMinHeight = 30;
    public const double LeafTitleSize = 11;
    public const double LeafDetailSize = 10;
    public const double HeaderTitleSize = 10.5;
    public const double HeaderDetailSize = 9.5;
}
