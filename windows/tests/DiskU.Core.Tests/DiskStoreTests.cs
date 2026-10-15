using DiskU.Core;

namespace DiskU.Core.Tests;

[TestClass]
public sealed class DiskStoreTests
{
    private const int Block = Fixture.Block;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task TrashingRemovesNodeAndUpdatesAncestorsAndSelection()
    {
        string root = Fixture.Make();
        try
        {
            var trashed = new List<string>();
            var store = new DiskStore(new DelegateTrasher(path => trashed.Add(Path.GetFileName(path))));
            store.Finish(await Scanner.ScanAsync(root));

            var tree = store.Root!;
            var photos = tree.Child("photos");
            var png = photos.Child("a.png");
            store.Selection = png;
            store.Zoom(photos);
            int revision = store.Revision;

            store.PendingTrash = png;
            store.ConfirmPendingTrash();

            CollectionAssert.AreEqual(new[] { "a.png" }, trashed);
            Assert.IsNull(store.PendingTrash);
            Assert.IsNull(store.ErrorMessage);
            Assert.AreEqual(revision + 1, store.Revision);
            Assert.AreEqual(2L * Block, photos.Size);
            Assert.AreEqual(11L * Block, tree.Size);
            Assert.AreEqual(7, tree.ItemCount);
            CollectionAssert.AreEqual(new[] { "b.jpg", "deep" }, photos.Names());
            CollectionAssert.AreEqual(new[] { "movie.mov", "Tool.app", "photos", "empty", "movie-alias.mov" }, tree.Names());
            Assert.AreSame(photos, store.Selection);
            Assert.AreSame(photos, store.ZoomRoot);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task TrashingZoomedFolderZoomsOutAndRefusalSurfacesError()
    {
        string root = Fixture.Make();
        try
        {
            bool refuse = false;
            var store = new DiskStore(new DelegateTrasher(path =>
            {
                if (refuse) throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
            }));
            store.Finish(await Scanner.ScanAsync(root));
            var tree = store.Root!;
            var photos = tree.Child("photos");
            var deep = photos.Child("deep");
            store.Zoom(deep);
            Assert.AreSame(deep, store.ZoomRoot);

            store.PendingTrash = photos;
            store.ConfirmPendingTrash();
            Assert.IsNull(store.ZoomRoot);
            Assert.AreEqual(9L * Block, tree.Size);
            CollectionAssert.AreEqual(new[] { "movie.mov", "Tool.app", "empty", "movie-alias.mov" }, tree.Names());

            refuse = true;
            var movie = tree.Children[0];
            store.PendingTrash = movie;
            store.ConfirmPendingTrash();
            // Adapted wording: "Recycle Bin" instead of the Mac's "Trash".
            Assert.IsNotNull(store.ErrorMessage);
            Assert.StartsWith("“movie.mov” couldn’t be moved to the Recycle Bin.", store.ErrorMessage, StringComparison.Ordinal);
            Assert.AreEqual(9L * Block, tree.Size);
            Assert.AreSame(tree, movie.Parent);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task LayoutTilesAreProportionalAndHitTestable()
    {
        string root = Fixture.Make();
        try
        {
            var store = new DiskStore(new DelegateTrasher(_ => { }));
            store.Finish(await Scanner.ScanAsync(root));

            var first = store.Layout(400, 300)!;
            Assert.AreEqual(new RectD(0, 0, 400, 300), first.Tiles[0].Rect);
            var movie = first.Tiles.First(t => t.Node.Name == "movie.mov");
            Assert.AreEqual(5.0 / 13.0 * 396 * 281, movie.Rect.Width * movie.Rect.Height, 0.5);
            Assert.AreEqual("movie.mov", first.TileAt(new PointD(movie.Rect.MidX, movie.Rect.MidY))?.Node.Name);
            Assert.AreSame(first, store.Layout(400, 300), "cached while nothing changes");
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task CancelledScanDoesNotOverwriteItsReplacement()
    {
        string first = Fixture.Make();
        string second = Fixture.Make();
        try
        {
            var dispatcher = new ManualDispatcher();
            var store = new DiskStore(new DelegateTrasher(_ => { }), dispatcher.Post);

            store.Scan(first);
            store.CancelScan();
            store.Scan(second);
            await dispatcher.RunUntilAsync(() => store.State is not ScanState.Scanning, Deadline);

            var scanned = store.Root ?? throw new AssertFailedException($"state is {store.State}");
            Assert.AreEqual(Path.GetFullPath(second), scanned.Path);
            Assert.AreEqual(13L * Block, scanned.Size);
        }
        finally
        {
            Fixture.Remove(first);
            Fixture.Remove(second);
        }
    }

    [TestMethod]
    public async Task OutlineListsLargestChildrenFirstAndRevealsHiddenSelections()
    {
        string root = Fixture.TempDir("DiskUOutline");
        try
        {
            string many = Path.Combine(root, "many");
            Directory.CreateDirectory(many);
            for (int i = 1; i <= 150; i++) File.WriteAllBytes(Path.Combine(many, $"f{i}.bin"), new byte[i * 4096]);
            var dispatcher = new ManualDispatcher();
            var store = new DiskStore(new DelegateTrasher(_ => { }), dispatcher.Post);
            store.Finish(await Scanner.ScanAsync(root));
            var folder = store.Root!.Children[0];

            Assert.AreEqual(100, store.OutlineLimit(folder));
            Assert.AreEqual("f51.bin", folder.Children.Take(100).Last().Name);

            var hidden = folder.Children[119];
            Assert.AreEqual("f31.bin", hidden.Name);
            store.Selection = hidden;
            Assert.AreEqual(120, store.OutlineLimit(folder));

            store.ShowMore(folder);
            Assert.AreEqual(620, store.OutlineLimit(folder));

            store.Scan(root);
            Assert.AreEqual(100, store.OutlineLimit(folder));
            store.CancelScan();
        }
        finally { Fixture.Remove(root); }
    }

    // ---- Windows additions ----

    [TestMethod]
    public async Task CancelledScanReturnsToIdleAndDropsItsResult()
    {
        string root = Fixture.Make();
        try
        {
            var dispatcher = new ManualDispatcher();
            var store = new DiskStore(new DelegateTrasher(_ => { }), dispatcher.Post);
            int revision = store.Revision;
            store.Scan(root);
            Assert.AreEqual(ScanPhase.Scanning, store.State.Phase);
            store.CancelScan();
            Assert.AreEqual(ScanPhase.Idle, store.State.Phase);

            // Give the cancelled scan time to finish and post whatever it has; none of it may land.
            await dispatcher.RunUntilAsync(() => false, TimeSpan.FromSeconds(1));
            Assert.AreEqual(ScanPhase.Idle, store.State.Phase);
            Assert.IsNull(store.Root);
            Assert.AreEqual(revision, store.Revision);
            Assert.AreEqual(Path.GetFullPath(root), Path.GetFullPath(store.LastScanned!), "cancel keeps lastScanned for Rescan");
            Assert.IsTrue(store.CanRescan);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task ReplacedScanDoesNotOverwriteItsReplacement()
    {
        string first = Fixture.Make();
        string second = Fixture.TempDir("DiskUSecond");
        try
        {
            Fixture.Write(second, "only.bin", 2);
            var dispatcher = new ManualDispatcher();
            var store = new DiskStore(new DelegateTrasher(_ => { }), dispatcher.Post);
            store.Scan(first);
            store.Scan(second); // no cancel: the first is replaced
            Assert.IsTrue(await dispatcher.RunUntilAsync(() => store.State is not ScanState.Scanning, Deadline));
            // Let the first scan finish too; its result must be dropped.
            await dispatcher.RunUntilAsync(() => false, TimeSpan.FromMilliseconds(500));
            Assert.AreEqual(Path.GetFullPath(second), store.Root?.Path);
            Assert.AreEqual(2L * Block, store.Root!.Size);
            Assert.AreEqual(1, store.Revision);
        }
        finally
        {
            Fixture.Remove(first);
            Fixture.Remove(second);
        }
    }

    [TestMethod]
    public async Task ScanReportsProgressThenFinishesOnTheDispatcher()
    {
        string root = Fixture.Make();
        try
        {
            var dispatcher = new ManualDispatcher();
            var store = new DiskStore(new DelegateTrasher(_ => { }), dispatcher.Post);
            var phases = new List<ScanPhase>();
            store.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DiskStore.State)) phases.Add(store.State.Phase); };
            store.Scan(root);
            Assert.IsTrue(await dispatcher.RunUntilAsync(() => store.State is ScanState.Done, Deadline));
            Assert.AreEqual(ScanPhase.Scanning, phases[0]);
            Assert.AreEqual(ScanPhase.Done, phases[^1]);
            Assert.IsTrue(store.IsExpanded(store.Root!), "root expanded after a scan");
            Assert.AreEqual(0, store.Inaccessible);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task ScanningAFileFailsWithAMessage()
    {
        string root = Fixture.Make();
        try
        {
            var dispatcher = new ManualDispatcher();
            var store = new DiskStore(new DelegateTrasher(_ => { }), dispatcher.Post);
            string file = Path.Combine(root, "movie.mov");
            store.Scan(file);
            Assert.IsTrue(await dispatcher.RunUntilAsync(() => store.State is ScanState.Failed, Deadline));
            Assert.AreEqual(Strings.NotAFolder(file), ((ScanState.Failed)store.State).Message);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task RootCannotBeTrashedAndTrasherIsNotCalled()
    {
        string root = Fixture.Make();
        try
        {
            var trashed = new List<string>();
            var store = new DiskStore(new DelegateTrasher(trashed.Add));
            store.Finish(await Scanner.ScanAsync(root));
            Assert.IsFalse(store.CanTrash(store.Root));
            store.RequestTrash(store.Root);
            Assert.IsNull(store.PendingTrash);
            store.Trash(store.Root!);
            Assert.IsEmpty(trashed);
            Assert.AreEqual(13L * Block, store.Root!.Size);

            var movie = store.Root.Child("movie.mov");
            store.RequestTrash(movie);
            Assert.AreSame(movie, store.PendingTrash);
            store.CancelPendingTrash();
            Assert.IsNull(store.PendingTrash);
            Assert.IsEmpty(trashed);

            store.Hovered = movie;
            store.Trash(movie);
            CollectionAssert.AreEqual(new[] { Path.Combine(Path.GetFullPath(root), "movie.mov") }, trashed, "the full path goes to the trasher");
            Assert.IsNull(store.Hovered);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task ZoomOutBreadcrumbsAndKindTotals()
    {
        string root = Fixture.Make();
        try
        {
            var store = new DiskStore(new DelegateTrasher(_ => { }));
            store.Finish(await Scanner.ScanAsync(root));
            var tree = store.Root!;
            var photos = tree.Child("photos");
            var deep = photos.Child("deep");

            store.Zoom(deep.Child("notes.txt")); // a file zooms to its folder
            Assert.AreSame(deep, store.ZoomRoot);
            CollectionAssert.AreEqual(new[] { tree, photos, deep }, store.Breadcrumbs.ToArray());
            store.ZoomOut();
            Assert.AreSame(photos, store.ZoomRoot);
            store.ZoomOut();
            Assert.IsNull(store.ZoomRoot);
            Assert.AreSame(tree, store.DisplayRoot);
            store.Zoom(tree.Child("empty")); // no children: nothing happens
            Assert.IsNull(store.ZoomRoot);
            store.ZoomTo(photos);
            Assert.AreSame(photos, store.ZoomRoot);
            store.ZoomTo(tree);
            Assert.IsNull(store.ZoomRoot);

            // Video 5 blocks, then images 3, documents 1, code 1 (Info.plist), other 3 (Tool); ties in Kind order.
            var totals = store.KindTotals.Select(t => (t.Kind, t.Bytes / Block)).ToArray();
            CollectionAssert.AreEqual(new (Kind, long)[] { (Kind.Video, 5), (Kind.Image, 3), (Kind.Other, 3), (Kind.Code, 1), (Kind.Document, 1) }, totals);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task OutlineRowsPageLargeFolders()
    {
        string root = Fixture.TempDir("DiskURows");
        try
        {
            string many = Path.Combine(root, "many");
            Directory.CreateDirectory(many);
            for (int i = 1; i <= 103; i++) File.WriteAllBytes(Path.Combine(many, $"f{i}.bin"), new byte[4096]);
            var store = new DiskStore(new DelegateTrasher(_ => { }));
            store.Finish(await Scanner.ScanAsync(root));
            var folder = store.Root!.Children[0];

            Assert.HasCount(2, store.OutlineRows()); // root + collapsed "many"
            store.SetExpanded(folder, true);
            var rows = store.OutlineRows();
            Assert.HasCount(1 + 1 + 100 + 1, rows);
            var more = rows[^1];
            Assert.IsTrue(more.IsShowMore);
            Assert.AreEqual(3, more.HiddenCount);
            Assert.AreEqual(3L * Block, more.HiddenBytes);
            Assert.AreEqual(2, more.Depth);
            Assert.AreEqual("3 smaller items", Strings.SmallerItems(more.HiddenCount));
            Assert.AreEqual("Show the next 3", Strings.ShowNext(more.HiddenCount));
            Assert.AreEqual(1.0 / 103, rows[2].Fraction, 1e-9);

            store.ShowMore(folder);
            Assert.HasCount(1 + 1 + 103, store.OutlineRows());
        }
        finally { Fixture.Remove(root); }
    }
}
