namespace DiskU.Core;

/// <summary>
/// Every user-visible string, verbatim from the Mac app except where Windows needs other words
/// (Trash → Recycle Bin, Finder → File Explorer, ⌘ → Ctrl, Full Disk Access → administrator).
/// Views use these as-is. Curly quotes and apostrophes (“ ” ’) and the single-character ellipsis (…)
/// are deliberate.
/// </summary>
public static class Strings
{
    public const string AppName = "DiskU";
    public const string WindowTitle = "DiskU";

    // Toolbar labels (tooltips: ToolbarTooltips.ByLabel).
    /// <summary>The sidebar toggle at the left of the title bar (browser only).</summary>
    public const string Sidebar = "Sidebar";
    public const string Cancel = "Cancel";
    public const string Rescan = "Rescan";
    public const string Home = "Home";
    public const string FullDisk = "Full Disk";
    public const string ScanEllipsis = "Scan…";
    /// <summary>Header of the Full Disk drop-down listing the other fixed drives.</summary>
    public const string ScanDrive = "Scan Drive";
    /// <summary>Like File Explorer: "Data (D:)", or "Local Disk (C:)" for a drive without a label.</summary>
    public static string DriveItem(string drive, string? label) => $"{(string.IsNullOrEmpty(label) ? LocalDisk : label)} ({drive.TrimEnd('\\')})";
    public const string LocalDisk = "Local Disk";

    // Welcome screen (state Idle).
    public const string WelcomeTitle = "See what’s filling your disk";
    public const string WelcomeSubtitle = "DiskU scans a folder and draws every file as a rectangle sized by the space it uses.";
    public const string ScanHomeFolder = "Scan Home Folder";
    public const string ScanFullDisk = "Scan Full Disk";
    public const string ChooseFolder = "Choose Folder…";

    // Administrator card on the welcome screen (replaces the Mac's Full Disk Access card).
    public const string AdminOn = "Running as administrator. DiskU can read every folder.";
    public const string AdminTitle = "Scan every folder";
    public const string AdminBody = "Some system and other users’ folders open only for administrators. Restart DiskU as administrator to include them.";
    public const string RestartAsAdministrator = "Restart as Administrator";

    // Scanning screen.
    public static string ScanningTitle(string path) => $"Scanning {path}";
    public const string FilesCaption = "files";
    public const string FoundCaption = "found";

    // Failure screen.
    public const string ScanFailed = "Scan Failed";
    public const string TryAgain = "Try Again";
    public static string NotAFolder(string path) => $"“{path}” isn’t a folder.";
    public static string FolderNotFound(string path) => $"The folder “{path}” doesn’t exist.";

    // Menus (File).
    public const string FileMenu = "File";
    public const string ScanFolderMenu = "Scan Folder…";
    public const string ScanHomeFolderMenu = "Scan Home Folder";
    public const string ScanFullDiskMenu = "Scan Full Disk";
    public const string RescanMenu = "Rescan";

    // Menus (Item) and the context menu.
    public const string ItemMenu = "Item";
    public const string ShowInExplorer = "Show in File Explorer";
    public const string Open = "Open";
    public const string CopyPath = "Copy Path";
    public const string Copied = "Copied";
    public const string ZoomOut = "Zoom Out";
    public const string ZoomIntoFolder = "Zoom Into Folder";
    public const string MoveToRecycleBinEllipsis = "Move to Recycle Bin…";

    // Keyboard shortcut texts shown in menus.
    public const string ShortcutScanFolder = "Ctrl+O";
    public const string ShortcutScanHome = "Ctrl+Shift+H";
    public const string ShortcutRescan = "Ctrl+R";
    public const string ShortcutShowInExplorer = "Ctrl+Shift+R";
    public const string ShortcutCopyPath = "Ctrl+Alt+C";
    public const string ShortcutOpen = "Ctrl+Down";
    public const string ShortcutZoomOut = "Ctrl+Up";
    public const string ShortcutMoveToRecycleBin = "Delete";

    // Trash confirmation and error.
    public static string TrashTitle(string name, long size) => $"Move “{name}” ({Format.Bytes(size)}) to the Recycle Bin?";
    public static string TrashMessage(long size, string path) => $"This frees about {Format.Bytes(size)}. You can restore it from the Recycle Bin.\n{path}";
    public const string MoveToRecycleBin = "Move to Recycle Bin";
    public const string TrashErrorTitle = "Couldn’t Move to Recycle Bin";
    public static string TrashError(string name, string reason) => $"“{name}” couldn’t be moved to the Recycle Bin. {reason}";
    public const string Ok = "OK";
    public static string NoRecycleBin(string path) => $"The drive of “{path}” has no Recycle Bin, so DiskU won’t delete it.";
    public const string TrashFailed = "Windows reported an error while moving it.";
    public const string TrashAborted = "The operation was cancelled.";

    // Folder picker.
    public const string PickerTitle = "Choose a folder to scan";
    public const string PickerButton = "Scan";

    // Inaccessible banner (browser).
    public static string FoldersCouldNotBeRead(int count) => count == 1 ? "1 folder couldn’t be read" : $"{Format.Count(count)} folders couldn’t be read";
    public const string SizesIncomplete = "Sizes are incomplete. Restart DiskU as administrator, then rescan.";

    // Update banner.
    public static string UpdateAvailable(AppVersion version) => $"DiskU {version} is available.";
    public const string ViewRelease = "View Release";
    public const string Dismiss = "Dismiss";

    // Inspector.
    public const string SelectAnItem = "Select an item to inspect it";
    public const string Contains = "Contains";
    public const string Share = "Share";
    public const string Modified = "Modified";
    public const string Where = "Where";
    public static string ShareOf(double fraction, string parentName) => $"{Format.Percent(fraction)} of {parentName}";
    public const string Reveal = "Reveal";
    public const string ByKind = "By Kind";

    // Outline.
    public static string SmallerItems(int count) => $"{Format.Count(count)} smaller items";
    public const string ShowMore = "Show more";
    public static string ShowNext(int count) => $"Show the next {Format.Count(Math.Min(count, DiskStore.OutlineMorePage))}";

    // Status bar.
    public const string StatusHint = "Click to select · Double-click a folder to zoom in · Space to open";
    public static string StatusTotals(FileNode node) => $"{Format.Items(node.ItemCount)} · {Format.Bytes(node.Size)}";
}

/// <summary>
/// Toolbar tooltips keyed by the button label (Mac: <c>ToolbarTooltips.byLabel</c>). A new toolbar
/// button needs an entry here.
/// </summary>
public static class ToolbarTooltips
{
    public static IReadOnlyDictionary<string, string> ByLabel { get; } = new Dictionary<string, string>
    {
        [Strings.Sidebar] = "Show or hide the sidebar",
        [Strings.Rescan] = "Scan the last folder again (Ctrl+R)",
        [Strings.Cancel] = "Stop scanning",
        [Strings.Home] = "Scan your home folder (Ctrl+Shift+H)",
        [Strings.FullDisk] = "Scan the whole startup disk",
        [Strings.ScanEllipsis] = "Choose a folder to scan (Ctrl+O)",
    };
}
