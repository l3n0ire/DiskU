using System.Globalization;
using DiskU.Core;

namespace DiskU.Core.Tests;

[TestClass]
public sealed class FormatTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    [TestMethod]
    [DataRow(0L, "0 bytes")]
    [DataRow(1L, "1 byte")]
    [DataRow(999L, "999 bytes")]
    [DataRow(1_000L, "1 KB")]
    [DataRow(20_480L, "20 KB")]
    [DataRow(999_499L, "999 KB")]
    [DataRow(999_999L, "1 MB")]
    [DataRow(44_000_000L, "44 MB")]
    [DataRow(106_958_848L, "107 MB")]
    [DataRow(471_900_000L, "471.9 MB")]
    [DataRow(996_100_000L, "996.1 MB")]
    [DataRow(2_000_000_000L, "2 GB")]
    [DataRow(13_170_000_000L, "13.17 GB")]
    [DataRow(83_650_000_000L, "83.65 GB")]
    [DataRow(1_500_000_000_000L, "1.5 TB")]
    public void BytesUseDecimalUnitsWithAdaptivePrecision(long count, string expected) =>
        Assert.AreEqual(expected, Format.Bytes(count, Us));

    [TestMethod]
    public void BytesUseTheCulturesDecimalSeparator() =>
        Assert.AreEqual("13,17 GB", Format.Bytes(13_170_000_000L, CultureInfo.GetCultureInfo("de-DE")));

    [TestMethod]
    public void ItemsSayFiles()
    {
        Assert.AreEqual("1 file", Format.Items(1, Us));
        Assert.AreEqual("0 files", Format.Items(0, Us));
        Assert.AreEqual("1,234 files", Format.Items(1234, Us));
    }

    [TestMethod]
    public void StringsKeepTheMacPunctuation()
    {
        Assert.AreEqual("1 folder couldn’t be read", Strings.FoldersCouldNotBeRead(1));
        // The ellipsis is the single character U+2026 and the apostrophe is U+2019, never "..." or "'".
        Assert.AreEqual('…', Strings.ScanEllipsis[^1]);
        Assert.HasCount(5, Strings.ScanEllipsis);
        Assert.AreEqual(8, Strings.WelcomeTitle.IndexOf('’', StringComparison.Ordinal));
        Assert.AreEqual("Choose a folder to scan (Ctrl+O)", ToolbarTooltips.ByLabel[Strings.ScanEllipsis]);
        Assert.AreEqual("Stop scanning", ToolbarTooltips.ByLabel[Strings.Cancel]);
        Assert.HasCount(6, ToolbarTooltips.ByLabel);
        Assert.IsTrue(Strings.TrashTitle("a.png", 8192).StartsWith("Move “a.png” (", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LaunchArgumentPicksTheFirstFolder()
    {
        string dir = Fixture.TempDir("DiskUArgs");
        try
        {
            Assert.AreEqual(dir, LaunchArguments.FolderFrom(["--flag", dir]));
            Assert.IsNull(LaunchArguments.FolderFrom([Path.Combine(dir, "missing")]));
            Assert.IsNull(LaunchArguments.FolderFrom([]));
            Assert.AreEqual(DiskStore.HomeFolder, LaunchArguments.FolderFrom(["~"]));
        }
        finally { Fixture.Remove(dir); }
    }
}
