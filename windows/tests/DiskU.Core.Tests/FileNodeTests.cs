using DiskU.Core;

namespace DiskU.Core.Tests;

[TestClass]
public sealed class FileNodeTests
{
    private static FileNode File(string name, long size) => new($@"C:\r\{name}", Kinds.ForFile(name), size, 1);

    private static FileNode Folder(string name, params FileNode[] children)
    {
        var node = new FileNode($@"C:\r\{name}", Kind.Folder);
        node.Adopt(children);
        return node;
    }

    [TestMethod]
    public void AdoptSumsAndSortsChildren()
    {
        var root = Folder("root", File("a.txt", 10), Folder("sub", File("b.mp4", 30), File("c.png", 50)), File("d.zip", 40));
        Assert.AreEqual(130L, root.Size);
        Assert.AreEqual(4, root.ItemCount);
        CollectionAssert.AreEqual(new[] { "sub", "d.zip", "a.txt" }, root.Names());
        CollectionAssert.AreEqual(new[] { "c.png", "b.mp4" }, root.Children[0].Names());
    }

    [TestMethod]
    public void RemovingNodeUpdatesEveryAncestorAndResorts()
    {
        var big = File("big.mov", 100);
        var inner = Folder("inner", big, File("small.txt", 5));
        var mid = Folder("mid", inner, File("m.png", 20));
        var root = Folder("root", mid, File("other.zip", 60));
        Assert.AreEqual(185L, root.Size);
        CollectionAssert.AreEqual(new[] { "mid", "other.zip" }, root.Names());

        big.RemoveFromParent();

        Assert.IsNull(big.Parent);
        Assert.AreEqual(5L, inner.Size);
        Assert.AreEqual(25L, mid.Size);
        Assert.AreEqual(85L, root.Size);
        Assert.AreEqual(3, root.ItemCount);
        CollectionAssert.AreEqual(new[] { "m.png", "inner" }, mid.Names());
        CollectionAssert.AreEqual(new[] { "other.zip", "mid" }, root.Names());
    }

    [TestMethod]
    public void KindRegistryMapsExtensions()
    {
        Assert.AreEqual(Kind.Video, Kinds.ForFile("Clip.MOV"));
        Assert.AreEqual(Kind.Archive, Kinds.ForFile("x.tar.gz"));
        Assert.AreEqual(Kind.Code, Kinds.ForFile("main.swift"));
        Assert.AreEqual(Kind.Other, Kinds.ForFile("README"));
    }

    // ---- Windows additions ----

    [TestMethod]
    public void NameTiesSortOrdinallyUppercaseFirst()
    {
        var root = Folder("root", File("photos", 4), File("Tool.app", 4), File("b.jpg", 4), File("deep", 4));
        CollectionAssert.AreEqual(new[] { "Tool.app", "b.jpg", "deep", "photos" }, root.Names());
    }

    [TestMethod]
    public void PathsComeFromTheParentChain()
    {
        var leaf = FileNode.Named("a.png", Kind.Image, 1, 1);
        var photos = FileNode.Named("photos", Kind.Folder);
        photos.Adopt([leaf]);
        var root = new FileNode(@"C:\Users\me", Kind.Folder);
        root.Adopt([photos]);
        Assert.AreEqual("me", root.Name);
        Assert.AreEqual(@"C:\Users\me\photos\a.png", leaf.Path);
        Assert.AreEqual(@"C:\", new FileNode(@"C:\", Kind.Folder).Name);
        Assert.AreEqual(@"C:\x", new FileNode(@"C:\x\", Kind.Folder).Path);
        Assert.IsTrue(leaf.IsDescendantOf(root));
        Assert.IsTrue(leaf.IsDescendantOf(leaf));
        Assert.IsFalse(root.IsDescendantOf(leaf));
        CollectionAssert.AreEqual(new[] { photos, root }, leaf.Ancestors);
    }

    [TestMethod]
    public void KindLabelsAndColorsMatchTheMac()
    {
        Assert.AreEqual("Archives & Disk Images", Kind.Archive.Label());
        Assert.AreEqual("System & Other", Kind.Other.Label());
        Assert.AreEqual("#6B7380", Kind.Folder.Color().Hex);
        Assert.AreEqual("#33B373", Kind.Image.Color().Hex);
        Assert.AreEqual("#E64D4D", Kind.Video.Color().Hex);
        Assert.AreEqual("#1A1A1A", Palette.FolderFill(9).Hex);
        Assert.AreEqual("#474747", Palette.HeaderFill(0).Hex);
        Assert.AreEqual(Kind.Other, Kinds.ForFile(".bashrc"));
        Assert.AreEqual(Kind.Other, Kinds.ForFile("setup.exe"));
    }
}
