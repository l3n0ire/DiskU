using System.Security.AccessControl;
using System.Security.Principal;
using DiskU.Core;

namespace DiskU.Core.Tests;

[TestClass]
public sealed class ScannerTests
{
    private const int Block = Fixture.Block;

    [TestMethod]
    public async Task ScanTotalsFixtureExactlyWithoutFollowingSymlinks()
    {
        string root = Fixture.Make();
        try
        {
            var result = await Scanner.ScanAsync(root);
            var tree = result.Root;

            Assert.AreEqual(13L * Block, tree.Size);
            Assert.AreEqual(8, tree.ItemCount);
            Assert.AreEqual(0, result.Inaccessible);
            CollectionAssert.AreEqual(new[] { "movie.mov", "Tool.app", "photos", "empty", "movie-alias.mov" }, tree.Names());
            CollectionAssert.AreEqual(new long[] { 20480, 16384, 16384, 0, 0 }, tree.Children.Select(c => c.Size).ToArray());

            var photos = tree.Child("photos");
            Assert.AreEqual(Kind.Folder, photos.Kind);
            Assert.AreEqual(4L * Block, photos.Size);
            CollectionAssert.AreEqual(new[] { "a.png", "b.jpg", "deep" }, photos.Names());

            // Adapted: Windows has no bundles, so Tool.app is an ordinary folder (Mac: kind bundle, no children).
            var app = tree.Child("Tool.app");
            Assert.AreEqual(Kind.Folder, app.Kind);
            Assert.AreEqual(2, app.ItemCount);
            CollectionAssert.AreEqual(new[] { "Contents" }, app.Names());

            var loop = photos.Child("deep").Child("loop");
            Assert.AreEqual(Kind.Other, loop.Kind);
            Assert.AreEqual(0L, loop.Size);
            Assert.IsEmpty(loop.Children);
            Assert.AreEqual(Kind.Video, tree.Child("movie.mov").Kind);
            Assert.AreEqual(Kind.Other, tree.Child("movie-alias.mov").Kind);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task UnreadableDirectoryCountsAsInaccessible()
    {
        // Adapted: the Mac uses chmod 000; Windows denies List Folder to the current user.
        // An elevated run enables the backup privilege, which bypasses the deny rule.
        if (Shell.IsElevated) Assert.Inconclusive("Run unelevated: administrators read past the deny rule.");
        string root = Fixture.Make();
        var locked = new DirectoryInfo(Path.Combine(root, @"photos\deep"));
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = locked.GetAccessControl();
        security.AddAccessRule(rule);
        locked.SetAccessControl(security);
        try
        {
            var result = await Scanner.ScanAsync(root);
            Assert.AreEqual(1, result.Inaccessible);
            Assert.AreEqual(12L * Block, result.Root.Size);
            Assert.IsEmpty(result.Root.Child("photos").Child("deep").Children);
        }
        finally
        {
            security = locked.GetAccessControl();
            security.RemoveAccessRule(rule);
            locked.SetAccessControl(security);
            Fixture.Remove(root);
        }
    }

    [TestMethod]
    public async Task ScanningAFileFails()
    {
        string root = Fixture.Make();
        try
        {
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Scanner.ScanAsync(Path.Combine(root, "movie.mov")));
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task ProgressReportsFinalCounts()
    {
        string root = Fixture.Make();
        try
        {
            var last = new ScanProgress();
            await Scanner.ScanAsync(root, p => last = p);
            Assert.AreEqual(8, last.FilesSeen);
            Assert.AreEqual(13L * Block, last.BytesSeen);
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task CancelledScanThrowsCancellation()
    {
        // Adapted: the Mac cancels the task right after starting it. The token is cancelled up front here,
        // because the tiny fixture could finish before a later Cancel() lands, making the test flaky.
        string root = Fixture.Make();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => Scanner.ScanAsync(root, null, cancellation.Token));
        }
        finally { Fixture.Remove(root); }
    }

    // ---- Windows additions ----

    [TestMethod]
    public async Task JunctionsAreLeavesAndNeverFollowed()
    {
        string root = Fixture.TempDir("DiskUJunction");
        string outside = Fixture.TempDir("DiskUJunctionTarget");
        try
        {
            Fixture.Write(root, @"target\data.bin", 2);
            Fixture.Write(outside, "big.bin", 7);
            Fixture.Junction(Path.Combine(root, "inside"), Path.Combine(root, "target"));
            Fixture.Junction(Path.Combine(root, "elsewhere"), outside);
            Fixture.Junction(Path.Combine(root, @"target\up"), root);

            var result = await Scanner.ScanAsync(root);
            var tree = result.Root;
            Assert.AreEqual(2L * Block, tree.Size);
            Assert.AreEqual(4, tree.ItemCount); // data.bin + three junctions
            CollectionAssert.AreEqual(new[] { "target", "elsewhere", "inside" }, tree.Names());
            foreach (var link in new[] { tree.Child("inside"), tree.Child("elsewhere"), tree.Child("target").Child("up") })
            {
                Assert.AreEqual(Kind.Other, link.Kind);
                Assert.AreEqual(0L, link.Size);
                Assert.AreEqual(1, link.ItemCount);
                Assert.IsEmpty(link.Children);
            }
        }
        finally
        {
            Fixture.Remove(root);
            Fixture.Remove(outside);
        }
    }

    [TestMethod]
    public async Task HardLinksCountOnce()
    {
        string root = Fixture.TempDir("DiskUHardLink");
        try
        {
            Fixture.Write(root, @"a\data.bin", 3);
            Directory.CreateDirectory(Path.Combine(root, "b"));
            Fixture.HardLink(Path.Combine(root, @"b\copy.bin"), Path.Combine(root, @"a\data.bin"));
            Fixture.HardLink(Path.Combine(root, @"b\copy2.bin"), Path.Combine(root, @"a\data.bin"));

            var last = new ScanProgress();
            var tree = (await Scanner.ScanAsync(root, p => last = p)).Root;
            Assert.AreEqual(3L * Block, tree.Size);
            Assert.AreEqual(3, tree.ItemCount);
            Assert.AreEqual(3L * Block, last.BytesSeen);
            var links = new[] { tree.Child("a").Child("data.bin"), tree.Child("b").Child("copy.bin"), tree.Child("b").Child("copy2.bin") };
            Assert.AreEqual(1, links.Count(l => l.Size == 3L * Block));
            Assert.AreEqual(2, links.Count(l => l.Size == 0));
        }
        finally { Fixture.Remove(root); }
    }

    [TestMethod]
    public async Task MissingFolderFailsWithItsPath()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"DiskUMissing-{Guid.NewGuid()}");
        var error = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Scanner.ScanAsync(missing));
        StringAssert.Contains(error.Message, missing);
    }

    [TestMethod]
    public async Task ManyFilesAcrossParallelDepthsAddUp()
    {
        // Deeper than ParallelDepth, so both the parallel and the synchronous paths run.
        string root = Fixture.TempDir("DiskUDeep");
        try
        {
            int files = 0;
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                    for (int c = 0; c < 2; c++)
                    {
                        Fixture.Write(root, $@"d{a}\d{b}\d{c}\x\y\f.bin", 1);
                        files++;
                    }
            var result = await Scanner.ScanAsync(root);
            Assert.AreEqual(files * (long)Block, result.Root.Size);
            Assert.AreEqual(files, result.Root.ItemCount);
            Assert.AreEqual(@"x", result.Root.Child("d0").Child("d0").Child("d0").Children[0].Name);
            Assert.AreEqual(Path.Combine(root, @"d2\d2\d1\x\y\f.bin"),
                result.Root.Child("d2").Child("d2").Child("d1").Child("x").Child("y").Child("f.bin").Path);
        }
        finally { Fixture.Remove(root); }
    }
}
