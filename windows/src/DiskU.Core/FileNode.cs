namespace DiskU.Core;

/// <summary>
/// One entry in the scanned tree. Folder sizes are always the sum of their children and change
/// only through <see cref="Propagate"/>. Equality is object identity (the default for classes).
/// </summary>
/// <remarks>
/// Unlike the Mac app, a node stores only its name, not its full path; <see cref="Path"/> is
/// derived from the parent chain. This avoids the Mac's per-node URL memory cost (docs/known-gaps.md).
/// A subtree is built by exactly one scanner task and handed to its parent only when complete;
/// after the scan, all mutation happens on the UI thread through <see cref="DiskStore"/>.
/// </remarks>
public sealed class FileNode
{
    private readonly List<FileNode> _children = [];
    /// <summary>Directory that contains this node, for nodes created from a full path (scan roots).</summary>
    private readonly string? _directory;

    /// <summary>Creates a node for a full path. Used for scan roots and detached nodes in tests.</summary>
    public FileNode(string path, Kind kind, long size = 0, int itemCount = 0)
    {
        string trimmed = System.IO.Path.TrimEndingDirectorySeparator(path);
        string name = System.IO.Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name))
        {
            // A drive root such as C:\ is its own name, like "/" on the Mac.
            Name = path;
            _directory = null;
        }
        else
        {
            Name = name;
            _directory = System.IO.Path.GetDirectoryName(trimmed);
        }
        Kind = kind;
        Size = size;
        ItemCount = itemCount;
    }

    private FileNode(Kind kind, string name, long size, int itemCount)
    {
        Name = name;
        Kind = kind;
        Size = size;
        ItemCount = itemCount;
    }

    /// <summary>Creates a node known only by name; its path comes from the parent that adopts it.</summary>
    public static FileNode Named(string name, Kind kind, long size = 0, int itemCount = 0) =>
        new(kind, name, size, itemCount);

    public string Name { get; }
    public Kind Kind { get; }

    /// <summary>Allocated bytes; for folders the sum of the children.</summary>
    public long Size { get; private set; }

    /// <summary>Files (and links) in this subtree; a file counts itself, a folder node counts 0.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Always sorted largest first (see <see cref="LargerFirst"/>).</summary>
    public IReadOnlyList<FileNode> Children => _children;

    public FileNode? Parent { get; private set; }

    /// <summary>Full path, derived by walking the parent chain.</summary>
    public string Path
    {
        get
        {
            if (Parent is { } parent) return System.IO.Path.Join(parent.Path, Name);
            return _directory is null ? Name : System.IO.Path.Join(_directory, Name);
        }
    }

    public bool IsFolder => Kind == Kind.Folder;

    /// <summary>Children for disclosure in the outline; null for leaves so no chevron is shown.</summary>
    public IReadOnlyList<FileNode>? OutlineChildren => IsFolder && _children.Count > 0 ? _children : null;

    /// <summary>Parent, grandparent, ..., root (nearest first).</summary>
    public List<FileNode> Ancestors
    {
        get
        {
            var chain = new List<FileNode>();
            for (var n = Parent; n is not null; n = n.Parent) chain.Add(n);
            return chain;
        }
    }

    /// <summary>True when this node is <paramref name="other"/> or lies inside it.</summary>
    public bool IsDescendantOf(FileNode other)
    {
        for (FileNode? n = this; n is not null; n = n.Parent)
            if (ReferenceEquals(n, other)) return true;
        return false;
    }

    /// <summary>Attaches <paramref name="newChildren"/> and folds their totals into this node and its ancestors.</summary>
    public void Adopt(IEnumerable<FileNode> newChildren)
    {
        long addedSize = 0;
        int addedItems = 0;
        foreach (var child in newChildren)
        {
            child.Parent = this;
            addedSize += child.Size;
            addedItems += child.ItemCount;
            _children.Add(child);
        }
        _children.Sort(Compare);
        Propagate(addedSize, addedItems);
    }

    /// <summary>Detaches this node and subtracts its totals from every ancestor. Its own totals are kept.</summary>
    public void RemoveFromParent()
    {
        if (Parent is not { } parent) return;
        int index = parent.IndexOfChild(this);
        if (index >= 0) parent._children.RemoveAt(index);
        parent.Propagate(-Size, -ItemCount);
        Parent = null;
    }

    /// <summary>
    /// The single place sizes change: applies a delta to this node and every ancestor, keeping each
    /// sibling list sorted largest first.
    /// </summary>
    public void Propagate(long size, int items)
    {
        for (FileNode? n = this; n is not null; n = n.Parent)
        {
            n.Size += size;
            n.ItemCount += items;
            if (n.Parent is { } p && size != 0) p.Reposition(n);
        }
    }

    /// <summary>Index of <paramref name="child"/> in <see cref="Children"/> by identity, or -1.</summary>
    public int IndexOfChild(FileNode child)
    {
        for (int i = 0; i < _children.Count; i++)
            if (ReferenceEquals(_children[i], child)) return i;
        return -1;
    }

    private void Reposition(FileNode child)
    {
        int index = IndexOfChild(child);
        if (index < 0) return;
        _children.RemoveAt(index);
        int insertAt = _children.Count;
        for (int j = 0; j < _children.Count; j++)
        {
            if (LargerFirst(child, _children[j])) { insertAt = j; break; }
        }
        _children.Insert(insertAt, child);
    }

    /// <summary>Size descending, ties by name in ordinal order (uppercase before lowercase), as on the Mac.</summary>
    public static bool LargerFirst(FileNode a, FileNode b) =>
        a.Size != b.Size ? a.Size > b.Size : string.CompareOrdinal(a.Name, b.Name) < 0;

    private static int Compare(FileNode a, FileNode b)
    {
        if (a.Size != b.Size) return a.Size > b.Size ? -1 : 1;
        return string.CompareOrdinal(a.Name, b.Name);
    }

    public override string ToString() => $"{Name} ({Size})";
}
