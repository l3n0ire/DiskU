namespace DiskU.Core;

public enum TileRole
{
    /// <summary>A folder drawn as a frame (full rect) with an optional header strip.</summary>
    Folder,
    /// <summary>A file, link, or empty folder drawn as one cushion.</summary>
    Leaf,
    /// <summary>The folder's children too small to draw, aggregated into one gray tile.</summary>
    Remainder,
}

/// <summary>One tile in paint order. For <see cref="TileRole.Remainder"/>, <see cref="Node"/> is the folder.</summary>
public sealed record TreemapTile(FileNode Node, RectD Rect, int Depth, TileRole Role, RectD? Header = null)
{
    public bool IsRemainder => Role == TileRole.Remainder;
}

/// <summary>
/// Flattened nested treemap in paint order: every folder precedes its contents, so the last tile
/// containing a point is the deepest one. Exact port of the Mac's TreemapLayout.
/// </summary>
public sealed class TreemapLayout
{
    public const double HeaderHeight = 15;
    public const double Inset = 2;
    /// <summary>Children whose area would be below this many square DIPs are not drawn individually.</summary>
    public const double MinArea = 1;

    public IReadOnlyList<TreemapTile> Tiles { get; }

    public TreemapLayout(FileNode root, RectD bounds)
    {
        var tiles = new List<TreemapTile>();
        Place(root, bounds, 0, tiles);
        Tiles = tiles;
    }

    /// <summary>The deepest tile containing the point (the last in paint order), or null.</summary>
    public TreemapTile? TileAt(PointD point)
    {
        for (int i = Tiles.Count - 1; i >= 0; i--)
            if (Tiles[i].Rect.Contains(point)) return Tiles[i];
        return null;
    }

    /// <summary>Rect of the first non-remainder tile for <paramref name="node"/>, or null when it isn't drawn.</summary>
    public RectD? RectOf(FileNode node)
    {
        foreach (var tile in Tiles)
            if (ReferenceEquals(tile.Node, node) && !tile.IsRemainder) return tile.Rect;
        return null;
    }

    private static void Place(FileNode node, RectD rect, int depth, List<TreemapTile> tiles)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        if (!node.IsFolder || node.Children.Count == 0 || node.Size <= 0)
        {
            tiles.Add(new TreemapTile(node, rect, depth, TileRole.Leaf));
            return;
        }

        var content = rect;
        RectD? header = null;
        if (rect.Width > 4 * Inset && rect.Height > 4 * Inset)
        {
            content = rect.Inset(Inset, Inset);
            if (content.Height > HeaderHeight * 2.5 && content.Width > 80)
            {
                header = new RectD(content.MinX, content.MinY, content.Width, HeaderHeight);
                content = new RectD(content.MinX, content.MinY + HeaderHeight, content.Width, content.Height - HeaderHeight);
            }
        }
        tiles.Add(new TreemapTile(node, rect, depth, TileRole.Folder, header));

        double scale = content.Width * content.Height / node.Size;
        var children = node.Children;
        int visible = 0;
        while (visible < children.Count && children[visible].Size * scale >= MinArea) visible++;

        var sizes = new List<double>(visible + 1);
        double sum = 0;
        for (int i = 0; i < visible; i++)
        {
            sizes.Add(children[i].Size);
            sum += children[i].Size;
        }
        double rest = node.Size - sum;
        if (rest > 0) sizes.Add(rest);

        var rects = Squarify.Layout(sizes, content);
        for (int i = 0; i < visible; i++) Place(children[i], rects[i], depth + 1, tiles);
        if (rest > 0 && rects.Length > 0)
        {
            var restRect = rects[^1];
            if (restRect.Width >= 1 && restRect.Height >= 1)
                tiles.Add(new TreemapTile(node, restRect, depth + 1, TileRole.Remainder));
        }
    }
}
