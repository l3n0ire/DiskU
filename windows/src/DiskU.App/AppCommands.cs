using DiskU.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace DiskU.App;

/// <summary>
/// Every user action in one place (Mac: Actions + DiskUCommands), shared by the toolbar, menus, context
/// menus, inspector and keyboard shortcuts.
/// </summary>
internal sealed class AppCommands(DiskStore store, Func<IntPtr> windowHandle)
{
    public DiskStore Store { get; } = store;

    public void ScanFolder()
    {
        if (FolderPicker.Choose(windowHandle()) is { } folder) Store.Scan(folder);
    }

    public void ScanHome() => Store.ScanHome();
    public void ScanFullDisk() => Store.ScanFullDisk();
    public void ScanDrive(string root) => Store.Scan(root);
    public void Rescan() => Store.Rescan();
    public void CancelScan() => Store.CancelScan();

    public void Reveal(FileNode? node) => Try(node, Shell.Reveal);
    public void Open(FileNode? node) => Try(node, Shell.Open);

    public void CopyPath(FileNode? node)
    {
        if (node is null) return;
        var package = new DataPackage();
        package.SetText(node.Path);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public void ZoomOut() => Store.ZoomOut();
    public void RequestTrash(FileNode? node) => Store.RequestTrash(node);

    public void RestartAsAdministrator()
    {
        // Rescan the same folder after the restart; the welcome screen restarts with nothing to scan.
        string? folder = Store.State is ScanState.Done or ScanState.Failed ? Store.LastScanned : null;
        if (Shell.RestartAsAdministrator(folder)) Application.Current.Exit();
    }

    /// <summary>The item context menu (Mac: ItemMenu), identical on outline rows and the treemap.</summary>
    public MenuFlyout ItemMenu(FileNode node)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item(Strings.ShowInExplorer, "", () => Reveal(node), Strings.ShortcutShowInExplorer));
        menu.Items.Add(Item(Strings.Open, "", () => Open(node), Strings.ShortcutOpen));
        menu.Items.Add(Item(Strings.CopyPath, "", () => CopyPath(node), Strings.ShortcutCopyPath));
        if (node.IsFolder && node.Children.Count > 0)
            menu.Items.Add(Item(Strings.ZoomIntoFolder, "", () => Store.Zoom(node)));
        menu.Items.Add(new MenuFlyoutSeparator());
        var trash = Item(Strings.MoveToRecycleBinEllipsis, "", () => RequestTrash(node), Strings.ShortcutMoveToRecycleBin);
        trash.IsEnabled = Store.CanTrash(node);
        if (trash.IsEnabled) trash.Style = Ui.AppStyle("DangerMenuItemStyle");
        menu.Items.Add(trash);
        return menu;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action action, string? shortcut = null)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        // The shortcut is only shown here; the window's menu bar owns the real accelerators.
        if (shortcut is not null) item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private static void Try(FileNode? node, Action<string> action)
    {
        if (node is null) return;
        try { action(node.Path); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Diagnostics.Log($"action failed for {node.Path}: {e.Message}");
        }
    }

    /// <summary>A Segoe Fluent Icons glyph.</summary>
    public static FontIcon Glyph(string glyph, double size = 16, Brush? foreground = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size };
        if (foreground is not null) icon.Foreground = foreground;
        return icon;
    }
}
