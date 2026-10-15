using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiskU.Core;

/// <summary>A row of the flattened sidebar outline: an item, or the "N smaller items / Show more" row.</summary>
/// <param name="Node">The item, or for a show-more row the folder whose children are hidden.</param>
/// <param name="Depth">Indent level; the scan root is 0.</param>
/// <param name="HiddenCount">Show-more rows only: how many children are hidden.</param>
/// <param name="HiddenBytes">Show-more rows only: their total size.</param>
public sealed record OutlineRow(FileNode Node, int Depth, bool IsShowMore, bool IsExpandable, bool IsExpanded, int HiddenCount = 0, long HiddenBytes = 0)
{
    /// <summary>Share of the parent for the proportion bar; 1 for the root or an empty parent.</summary>
    public double Fraction => Node.Parent is { Size: > 0 } parent ? (double)Node.Size / parent.Size : 1;
}

/// <summary>
/// Owns the scanned tree and all UI state that refers into it (port of the Mac's DiskStore). FileNode is
/// not observable; every tree mutation goes through here and bumps <see cref="Revision"/>.
/// <para>Threading: call every member on the UI thread. Scan progress and results arrive through the
/// dispatch callback given to the constructor (default: the SynchronizationContext current at construction,
/// so constructing on the WinUI thread is enough). Change notifications are raised on that thread.</para>
/// </summary>
public sealed class DiskStore : INotifyPropertyChanged
{
    public const int OutlinePage = 100;
    public const int OutlineMorePage = 500;

    private readonly ITrasher _trasher;
    private readonly Action<Action> _dispatch;
    private readonly HashSet<FileNode> _expanded = [];
    private readonly Dictionary<FileNode, int> _outlineLimits = [];
    private CancellationTokenSource? _scanCancellation;
    private int _scanGeneration;
    private (FileNode Root, double Width, double Height, int Revision, TreemapLayout Layout)? _layoutCache;
    private (int Revision, IReadOnlyList<(Kind Kind, long Bytes)> Totals)? _kindTotalsCache;

    private ScanState _state = ScanState.Idle;
    private int _revision;
    private FileNode? _selection;
    private FileNode? _zoomRoot;
    private FileNode? _hovered;
    private string? _lastScanned;
    private FileNode? _pendingTrash;
    private string? _errorMessage;

    /// <param name="trasher">Moves items to the Recycle Bin; tests inject a recording one. Default: <see cref="RecycleBinTrasher"/>.</param>
    /// <param name="dispatch">Runs an action on the UI thread. Default: posts to the current SynchronizationContext, or runs inline when there is none.</param>
    public DiskStore(ITrasher? trasher = null, Action<Action>? dispatch = null)
    {
        _trasher = trasher ?? new RecycleBinTrasher();
        if (dispatch is null)
        {
            var context = SynchronizationContext.Current;
            dispatch = context is null ? action => action() : action => context.Post(_ => action(), null);
        }
        _dispatch = dispatch;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScanState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnChanged();
            OnChanged(nameof(Root));
            OnChanged(nameof(DisplayRoot));
            OnChanged(nameof(Inaccessible));
        }
    }

    /// <summary>Bumped on every tree mutation (finish, trash). Views showing sizes refresh on it.</summary>
    public int Revision
    {
        get => _revision;
        private set { _revision = value; OnChanged(); }
    }

    /// <summary>Setting a node also expands its ancestors and lifts any outline limit that hides it.</summary>
    public FileNode? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            if (value is not null) ExpandAncestors(value);
            OnChanged();
        }
    }

    /// <summary>The zoomed-in folder; null means the root.</summary>
    public FileNode? ZoomRoot
    {
        get => _zoomRoot;
        set { _zoomRoot = value; OnChanged(); OnChanged(nameof(DisplayRoot)); OnChanged(nameof(CanZoomOut)); }
    }

    public FileNode? Hovered
    {
        get => _hovered;
        set { if (!ReferenceEquals(_hovered, value)) { _hovered = value; OnChanged(); } }
    }

    /// <summary>Folders open in the outline. Change with <see cref="SetExpanded"/>.</summary>
    public IReadOnlySet<FileNode> Expanded => _expanded;

    /// <summary>How many children each folder lists in the outline when not the default <see cref="OutlinePage"/>.</summary>
    public IReadOnlyDictionary<FileNode, int> OutlineLimits => _outlineLimits;

    /// <summary>Path of the last scan started; not cleared by cancel.</summary>
    public string? LastScanned
    {
        get => _lastScanned;
        private set { _lastScanned = value; OnChanged(); OnChanged(nameof(CanRescan)); }
    }

    /// <summary>Item awaiting the user's confirmation before it goes to the Recycle Bin. Drives the dialog.</summary>
    public FileNode? PendingTrash
    {
        get => _pendingTrash;
        set { _pendingTrash = value; OnChanged(); }
    }

    /// <summary>Drives the "Couldn’t Move to Recycle Bin" alert; set to null to dismiss it.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnChanged(); }
    }

    /// <summary>The scanned root when <see cref="State"/> is Done, else null.</summary>
    public FileNode? Root => _state is ScanState.Done done ? done.Root : null;

    /// <summary>Folders the last finished scan couldn't read (0 unless Done).</summary>
    public int Inaccessible => _state is ScanState.Done done ? done.Inaccessible : 0;

    /// <summary>The folder the treemap currently shows.</summary>
    public FileNode? DisplayRoot => _zoomRoot ?? Root;

    public bool CanRescan => _lastScanned is not null;
    public bool CanZoomOut => _zoomRoot is not null;

    // ---- Scanning ----

    /// <summary>The user's profile folder (Home button).</summary>
    public static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The system drive root, e.g. C:\ (Full Disk button).</summary>
    public static string SystemDrive =>
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) is { Length: > 0 } root ? root : @"C:\";

    /// <summary>Ready fixed drives for the Full Disk drop-down, system drive first.</summary>
    public static IReadOnlyList<DriveInfo> FixedDrives()
    {
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToList();
        drives.Sort((a, b) =>
        {
            bool aSystem = string.Equals(a.Name, SystemDrive, StringComparison.OrdinalIgnoreCase);
            bool bSystem = string.Equals(b.Name, SystemDrive, StringComparison.OrdinalIgnoreCase);
            return aSystem != bSystem ? (aSystem ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return drives;
    }

    public void ScanHome() => Scan(HomeFolder);
    public void ScanFullDisk() => Scan(SystemDrive);

    /// <summary>Starts a scan, replacing any running one. Progress and the result arrive through the dispatcher.</summary>
    public void Scan(string path)
    {
        _scanCancellation?.Cancel();
        LastScanned = path;
        Selection = null;
        ZoomRoot = null;
        Hovered = null;
        _expanded.Clear();
        OnChanged(nameof(Expanded));
        _outlineLimits.Clear();
        OnChanged(nameof(OutlineLimits));
        State = new ScanState.Scanning(new ScanProgress());
        int generation = ++_scanGeneration;
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        // A superseded or cancelled scan must not overwrite the state of the one that replaced it.
        _ = RunScanAsync(path, generation, cancellation.Token);
    }

    private async Task RunScanAsync(string path, int generation, CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() => Scanner.ScanAsync(path, progress => _dispatch(() =>
            {
                if (_scanGeneration == generation) State = new ScanState.Scanning(progress);
            }), token), token).ConfigureAwait(false);
            _dispatch(() =>
            {
                if (_scanGeneration == generation) Finish(result);
            });
        }
        catch (Exception e)
        {
            _dispatch(() =>
            {
                if (_scanGeneration != generation) return;
                State = e is OperationCanceledException ? ScanState.Idle : new ScanState.Failed(e.Message);
            });
        }
    }

    /// <summary>Installs a finished scan; also the entry point for tools and tests that scan directly.</summary>
    public void Finish(ScanResult result)
    {
        State = new ScanState.Done(result.Root, result.Inaccessible);
        _expanded.Clear();
        _expanded.Add(result.Root);
        OnChanged(nameof(Expanded));
        Revision++;
    }

    /// <summary>Stops the scan and returns to the start screen.</summary>
    public void CancelScan()
    {
        _scanCancellation?.Cancel();
        _scanCancellation = null;
        _scanGeneration++;
        State = ScanState.Idle;
    }

    public void Rescan()
    {
        if (_lastScanned is { } path) Scan(path);
    }

    // ---- Zoom ----

    /// <summary>Zooms into a folder with children, or into a file's parent folder.</summary>
    public void Zoom(FileNode node)
    {
        var folder = node.IsFolder ? node : node.Parent;
        if (folder is null || folder.Children.Count == 0) return;
        ZoomRoot = ReferenceEquals(folder, Root) ? null : folder;
    }

    /// <summary>Item ▸ Zoom Out: one level up; reaching the root clears the zoom.</summary>
    public void ZoomOut()
    {
        if (_zoomRoot is null) return;
        ZoomRoot = ReferenceEquals(_zoomRoot.Parent, Root) ? null : _zoomRoot.Parent;
    }

    /// <summary>Breadcrumb click.</summary>
    public void ZoomTo(FileNode crumb) => ZoomRoot = ReferenceEquals(crumb, Root) ? null : crumb;

    /// <summary>Breadcrumb trail: root first, display root last.</summary>
    public IReadOnlyList<FileNode> Breadcrumbs
    {
        get
        {
            if (DisplayRoot is not { } display) return [];
            var trail = new List<FileNode>();
            for (FileNode? n = display; n is not null; n = n.Parent)
            {
                trail.Add(n);
                if (ReferenceEquals(n, Root)) break;
            }
            trail.Reverse();
            return trail;
        }
    }

    // ---- Recycle Bin ----

    /// <summary>False for the scan root, which can't be trashed.</summary>
    public bool CanTrash(FileNode? node) => node is not null && Root is not null && !ReferenceEquals(node, Root);

    /// <summary>Asks for confirmation (sets <see cref="PendingTrash"/>) unless the node is the root.</summary>
    public void RequestTrash(FileNode? node)
    {
        if (CanTrash(node)) PendingTrash = node;
    }

    public void CancelPendingTrash() => PendingTrash = null;

    /// <summary>Moves the item to the Recycle Bin and removes it from the tree. Throws if the Recycle Bin refuses.</summary>
    public void Trash(FileNode node)
    {
        if (ReferenceEquals(node, Root)) return;
        _trasher.Trash(node.Path);
        var parent = node.Parent;
        if (_zoomRoot is not null && _zoomRoot.IsDescendantOf(node)) ZoomRoot = ReferenceEquals(parent, Root) ? null : parent;
        if (_selection is not null && _selection.IsDescendantOf(node)) Selection = parent;
        if (_hovered is not null && _hovered.IsDescendantOf(node)) Hovered = null;
        if (_expanded.Remove(node)) OnChanged(nameof(Expanded));
        node.RemoveFromParent();
        Revision++;
    }

    public void ConfirmPendingTrash()
    {
        if (_pendingTrash is not { } node) return;
        PendingTrash = null;
        try
        {
            Trash(node);
        }
        catch (Exception e)
        {
            ErrorMessage = Strings.TrashError(node.Name, e.Message);
        }
    }

    // ---- Treemap ----

    /// <summary>Layout for the current display root, recomputed only when the root, size, or tree changes.</summary>
    public TreemapLayout? Layout(double width, double height)
    {
        if (DisplayRoot is not { } display) return null;
        if (_layoutCache is { } cache && ReferenceEquals(cache.Root, display) && cache.Width == width && cache.Height == height && cache.Revision == _revision)
            return cache.Layout;
        var layout = new TreemapLayout(display, new RectD(0, 0, width, height));
        _layoutCache = (display, width, height, _revision, layout);
        return layout;
    }

    /// <summary>Bytes per kind across the whole scan (not the zoom), largest first; ties in Kind order.</summary>
    public IReadOnlyList<(Kind Kind, long Bytes)> KindTotals
    {
        get
        {
            if (_kindTotalsCache is { } cache && cache.Revision == _revision) return cache.Totals;
            var totals = new long[Kinds.All.Count];
            var stack = new Stack<FileNode>();
            if (Root is { } root) stack.Push(root);
            while (stack.TryPop(out var node))
            {
                if (node.Children.Count == 0) totals[(int)node.Kind] += node.Size;
                else foreach (var child in node.Children) stack.Push(child);
            }
            var sorted = Kinds.All.Where(k => totals[(int)k] > 0)
                .Select(k => (Kind: k, Bytes: totals[(int)k]))
                .OrderByDescending(t => t.Bytes).ThenBy(t => t.Kind)
                .ToList();
            _kindTotalsCache = (_revision, sorted);
            return sorted;
        }
    }

    // ---- Outline ----

    public int OutlineLimit(FileNode folder) => _outlineLimits.TryGetValue(folder, out int limit) ? limit : OutlinePage;

    /// <summary>"Show more": lists the next <see cref="OutlineMorePage"/> children.</summary>
    public void ShowMore(FileNode folder)
    {
        _outlineLimits[folder] = OutlineLimit(folder) + OutlineMorePage;
        OnChanged(nameof(OutlineLimits));
    }

    public bool IsExpanded(FileNode node) => _expanded.Contains(node);

    public void SetExpanded(FileNode node, bool expanded)
    {
        bool changed = expanded ? _expanded.Add(node) : _expanded.Remove(node);
        if (changed) OnChanged(nameof(Expanded));
    }

    /// <summary>
    /// The visible sidebar rows, flattened for a virtualized list: the root, then each expanded folder's
    /// first <see cref="OutlineLimit"/> children, then a show-more row when more are hidden. Rebuild it when
    /// Root, Expanded, OutlineLimits or Revision change.
    /// </summary>
    public IReadOnlyList<OutlineRow> OutlineRows()
    {
        var rows = new List<OutlineRow>();
        if (Root is { } root) AddRows(root, 0, rows);
        return rows;
    }

    private void AddRows(FileNode node, int depth, List<OutlineRow> rows)
    {
        bool expandable = node.OutlineChildren is not null;
        bool expanded = expandable && _expanded.Contains(node);
        rows.Add(new OutlineRow(node, depth, false, expandable, expanded));
        if (!expanded) return;
        var children = node.Children;
        int limit = Math.Min(OutlineLimit(node), children.Count);
        for (int i = 0; i < limit; i++) AddRows(children[i], depth + 1, rows);
        if (children.Count > limit)
        {
            long hidden = 0;
            for (int i = limit; i < children.Count; i++) hidden += children[i].Size;
            rows.Add(new OutlineRow(node, depth + 1, true, false, false, children.Count - limit, hidden));
        }
    }

    /// <summary>Opens every ancestor and lifts any outline limit that would hide the node.</summary>
    private void ExpandAncestors(FileNode node)
    {
        bool expandedChanged = false;
        foreach (var ancestor in node.Ancestors) expandedChanged |= _expanded.Add(ancestor);
        bool limitsChanged = false;
        for (var child = node; child.Parent is { } parent; child = parent)
        {
            int index = parent.IndexOfChild(child);
            if (index >= OutlineLimit(parent))
            {
                _outlineLimits[parent] = index + 1;
                limitsChanged = true;
            }
        }
        if (expandedChanged) OnChanged(nameof(Expanded));
        if (limitsChanged) OnChanged(nameof(OutlineLimits));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
