using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskU.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DiskU.App;

/// <summary>
/// The window (Mac: ContentView + ScanToolbar + DiskUCommands): title bar with menus and the toolbar, the
/// update banner, and one view per <see cref="ScanState"/>. Owns the store and the update checker.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly DiskStore _store;
    private readonly AppCommands _commands;
    private readonly UpdateChecker _updates = new();
    private readonly Stopwatch _scanClock = new();
    private bool _dialogOpen;

    public MainWindow(string? launchFolder)
    {
        InitializeComponent();
        // Scan progress and results are marshalled to this window's UI thread.
        _store = new DiskStore(dispatch: action => DispatcherQueue.TryEnqueue(() => action()));
        _commands = new AppCommands(_store, () => WinRT.Interop.WindowNative.GetWindowHandle(this));

        Title = Strings.WindowTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ApplyThemeOverride();
        SizeWindow();

        SetTexts();
        Browser.Initialize(_store, _commands);
        _store.PropertyChanged += OnStoreChanged;
        _updates.PropertyChanged += (_, _) => UpdateUpdateBar();
        ShowState();
        UpdateCommands();

        if (launchFolder is not null) _store.Scan(launchFolder);
        var version = AppVersion.Parse(Diagnostics.CurrentVersionOverride) ?? UpdateChecker.CurrentVersion;
        _ = _updates.CheckAsync(version);
    }

    // ---- Setup ----

    private void SizeWindow()
    {
        // AppWindow sizes are physical pixels; the Mac sizes are points (DIPs).
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Round(900 * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(560 * scale);
        }
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)Math.Round(1200 * scale), area.Width);
        int height = Math.Min((int)Math.Round(800 * scale), area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    private void ApplyThemeOverride()
    {
        if (Diagnostics.Theme is not { } theme) return;
        if (Enum.TryParse<ElementTheme>(theme, ignoreCase: true, out var requested) && requested != ElementTheme.Default)
        {
            RootGrid.RequestedTheme = requested;
            AppWindow.TitleBar.PreferredTheme = requested == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        }
    }

    private void SetTexts()
    {
        AppTitleBar.Title = Strings.AppName;
        FileMenu.Title = Strings.FileMenu;
        ItemMenu.Title = Strings.ItemMenu;
        ScanFolderItem.Text = Strings.ScanFolderMenu;
        ScanHomeItem.Text = Strings.ScanHomeFolderMenu;
        ScanFullDiskItem.Text = Strings.ScanFullDiskMenu;
        RescanItem.Text = Strings.RescanMenu;
        RevealItem.Text = Strings.ShowInExplorer;
        OpenItem.Text = Strings.Open;
        CopyPathItem.Text = Strings.CopyPath;
        ZoomOutItem.Text = Strings.ZoomOut;
        TrashItem.Text = Strings.MoveToRecycleBinEllipsis;
        ScanFolderItem.KeyboardAcceleratorTextOverride = Strings.ShortcutScanFolder;
        ScanHomeItem.KeyboardAcceleratorTextOverride = Strings.ShortcutScanHome;
        RescanItem.KeyboardAcceleratorTextOverride = Strings.ShortcutRescan;
        RevealItem.KeyboardAcceleratorTextOverride = Strings.ShortcutShowInExplorer;
        OpenItem.KeyboardAcceleratorTextOverride = Strings.ShortcutOpen;
        CopyPathItem.KeyboardAcceleratorTextOverride = Strings.ShortcutCopyPath;
        ZoomOutItem.KeyboardAcceleratorTextOverride = Strings.ShortcutZoomOut;
        TrashItem.KeyboardAcceleratorTextOverride = Strings.ShortcutMoveToRecycleBin;

        // Toolbar: icon-only; the label is the accessible name and the tooltip comes from the single table.
        ConfigureToolbarButton(CancelButton, Strings.Cancel);
        ConfigureToolbarButton(RescanButton, Strings.Rescan);
        ConfigureToolbarButton(HomeButton, Strings.Home);
        ConfigureToolbarButton(FullDiskButton, Strings.FullDisk);
        ConfigureToolbarButton(ScanButton, Strings.ScanEllipsis);

        WelcomeTitle.Text = Strings.WelcomeTitle;
        WelcomeSubtitle.Text = Strings.WelcomeSubtitle;
        WelcomeHome.Content = Label("", Strings.ScanHomeFolder);
        WelcomeFullDisk.Content = Label("", Strings.ScanFullDisk);
        WelcomeChoose.Content = Label("", Strings.ChooseFolder);
        bool elevated = Shell.IsElevated;
        AdminCard.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;
        AdminOnRow.Visibility = elevated ? Visibility.Visible : Visibility.Collapsed;
        AdminTitle.Text = Strings.AdminTitle;
        AdminBody.Text = Strings.AdminBody;
        AdminButton.Content = Label("", Strings.RestartAsAdministrator);
        AdminOn.Text = Strings.AdminOn;

        FilesCaption.Text = Strings.FilesCaption;
        BytesCaption.Text = Strings.FoundCaption;
        ScanningCancel.Content = Strings.Cancel;
        FailureTitle.Text = Strings.ScanFailed;
        TryAgainButton.Content = Strings.TryAgain;
        ViewReleaseButton.Content = Strings.ViewRelease;
        if (Application.Current.Resources.TryGetValue("InfoBarCloseButtonStyle", out var closeStyle) && closeStyle is Style baseStyle)
        {
            var style = new Style(typeof(Button)) { BasedOn = baseStyle };
            style.Setters.Add(new Setter(ToolTipService.ToolTipProperty, Strings.Dismiss));
            UpdateBar.CloseButtonStyle = style;
        }
    }

    private static void ConfigureToolbarButton(Control button, string label)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, ToolbarTooltips.ByLabel[label]);
    }

    /// <summary>The title bar's built-in pane button is the sidebar toggle; give it the label and tooltip from the table.</summary>
    private void ConfigurePaneToggle()
    {
        // The button is created lazily the first time it shows, so look again after the next layout pass.
        if (FindByName(AppTitleBar, "PART_PaneToggleButton") is Button toggle) ConfigureToolbarButton(toggle, Strings.Sidebar);
        else AppTitleBar.LayoutUpdated += RetryPaneToggle;
    }

    private void RetryPaneToggle(object? sender, object e)
    {
        if (FindByName(AppTitleBar, "PART_PaneToggleButton") is not Button toggle) return;
        AppTitleBar.LayoutUpdated -= RetryPaneToggle;
        ConfigureToolbarButton(toggle, Strings.Sidebar);
    }

    private static FrameworkElement? FindByName(DependencyObject parent, string name)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } element && n == name) return element;
            if (FindByName(child, name) is { } found) return found;
        }
        return null;
    }

    private static StackPanel Label(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = text });
        return panel;
    }

    // ---- State ----

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DiskStore.State):
                ShowState();
                UpdateCommands();
                break;
            case nameof(DiskStore.Selection):
            case nameof(DiskStore.ZoomRoot):
            case nameof(DiskStore.LastScanned):
                UpdateCommands();
                break;
            case nameof(DiskStore.PendingTrash):
                if (_store.PendingTrash is not null) DispatcherQueue.TryEnqueue(() => _ = ConfirmTrashAsync());
                break;
            case nameof(DiskStore.ErrorMessage):
                if (_store.ErrorMessage is not null) DispatcherQueue.TryEnqueue(() => _ = ShowErrorAsync());
                break;
        }
    }

    private void ShowState()
    {
        var state = _store.State;
        WelcomeView.Visibility = state.Phase == ScanPhase.Idle ? Visibility.Visible : Visibility.Collapsed;
        ScanningView.Visibility = state.Phase == ScanPhase.Scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanBar.Visibility = ScanningView.Visibility;
        FailureView.Visibility = state.Phase == ScanPhase.Failed ? Visibility.Visible : Visibility.Collapsed;
        Browser.Visibility = state.Phase == ScanPhase.Done ? Visibility.Visible : Visibility.Collapsed;
        AppTitleBar.IsPaneToggleButtonVisible = state.Phase == ScanPhase.Done;
        if (state.Phase == ScanPhase.Done) DispatcherQueue.TryEnqueue(ConfigurePaneToggle);

        switch (state)
        {
            case ScanState.Scanning scanning:
                if (!_scanClock.IsRunning)
                {
                    _scanClock.Restart();
                    // Keyboard focus on Cancel, so Escape (its accelerator) works right away, even at launch.
                    DispatcherQueue.TryEnqueue(() => ScanningCancel.Focus(FocusState.Programmatic));
                }
                ScanningTitle.Text = Strings.ScanningTitle(_store.LastScanned ?? "");
                FilesValue.Text = Format.Count(scanning.Progress.FilesSeen);
                BytesValue.Text = Format.Bytes(scanning.Progress.BytesSeen);
                CurrentPath.Text = scanning.Progress.CurrentPath;
                break;
            case ScanState.Failed failed:
                _scanClock.Reset();
                FailureMessage.Text = failed.Message;
                break;
            case ScanState.Done done:
                if (_scanClock.IsRunning)
                    Diagnostics.Log($"scan {_store.LastScanned}: {done.Root.ItemCount} files, {done.Root.Size} bytes, {done.Inaccessible} unreadable, {_scanClock.Elapsed.TotalSeconds:0.00}s");
                _scanClock.Reset();
                break;
            default:
                _scanClock.Reset();
                break;
        }
    }

    private void UpdateCommands()
    {
        bool scanning = _store.State is ScanState.Scanning;
        CancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        RescanButton.Visibility = scanning ? Visibility.Collapsed : Visibility.Visible;
        RescanButton.IsEnabled = _store.CanRescan;
        RescanItem.IsEnabled = _store.CanRescan;
        bool selected = _store.Selection is not null && _store.State is ScanState.Done;
        RevealItem.IsEnabled = selected;
        OpenItem.IsEnabled = selected;
        CopyPathItem.IsEnabled = selected;
        ZoomOutItem.IsEnabled = _store.CanZoomOut;
        TrashItem.IsEnabled = _store.CanTrash(_store.Selection);
    }

    private void UpdateUpdateBar()
    {
        if (_updates.Available is { } release) UpdateBar.Title = Strings.UpdateAvailable(release.Version);
        UpdateBar.IsOpen = _updates.ShowBanner;
    }

    // ---- Dialogs ----

    private async Task ConfirmTrashAsync()
    {
        if (_dialogOpen || _store.PendingTrash is not { } node) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = Strings.TrashTitle(node.Name, node.Size),
                Content = TrashMessage(Strings.TrashMessage(node.Size, node.Path)),
                PrimaryButtonText = Strings.MoveToRecycleBin,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Close,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && ReferenceEquals(_store.PendingTrash, node)) _store.ConfirmPendingTrash();
            else _store.CancelPendingTrash();
        }
        finally
        {
            _dialogOpen = false;
        }
        if (_store.ErrorMessage is not null) await ShowErrorAsync();
    }

    /// <summary>The message's first line as body text, the path below it in secondary color.</summary>
    private static StackPanel TrashMessage(string message)
    {
        int newline = message.IndexOf('\n');
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = newline < 0 ? message : message[..newline], TextWrapping = TextWrapping.Wrap });
        if (newline >= 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = message[(newline + 1)..],
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Style = Ui.AppStyle("SecondaryCaptionStyle"),
            });
        }
        return panel;
    }

    private async Task ShowErrorAsync()
    {
        if (_dialogOpen || _store.ErrorMessage is not { } message) return;
        _dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = Strings.TrashErrorTitle,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                CloseButtonText = Strings.Ok,
                DefaultButton = ContentDialogButton.Close,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
            _store.ErrorMessage = null;
        }
    }

    // ---- Keyboard ----

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Escape cancels a running scan wherever focus is (Mac: Cancel is the cancel action).
        if (e.Key == VirtualKey.Escape && _store.State is ScanState.Scanning)
        {
            _commands.CancelScan();
            e.Handled = true;
            return;
        }
        // Space opens the selection (the Mac's Quick Look key), unless a button or text field has focus.
        if (e.Key != VirtualKey.Space || _store.State is not ScanState.Done || _store.Selection is null) return;
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        if (focused is ButtonBase or TextBox) return;
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu) || IsDown(VirtualKey.Shift)) return;
        _commands.Open(_store.Selection);
        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ---- Handlers ----

    private void ScanFolder_Click(object sender, RoutedEventArgs e) => _commands.ScanFolder();
    private void ScanHome_Click(object sender, RoutedEventArgs e) => _commands.ScanHome();
    private void ScanFullDisk_Click(object sender, RoutedEventArgs e) => _commands.ScanFullDisk();
    private void FullDisk_Click(SplitButton sender, SplitButtonClickEventArgs args) => _commands.ScanFullDisk();
    private void Rescan_Click(object sender, RoutedEventArgs e) => _commands.Rescan();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _commands.CancelScan();
    private void Reveal_Click(object sender, RoutedEventArgs e) => _commands.Reveal(_store.Selection);
    private void Open_Click(object sender, RoutedEventArgs e) => _commands.Open(_store.Selection);
    private void CopyPath_Click(object sender, RoutedEventArgs e) => _commands.CopyPath(_store.Selection);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => _commands.ZoomOut();
    private void Trash_Click(object sender, RoutedEventArgs e) => _commands.RequestTrash(_store.Selection);
    private void RestartAdmin_Click(object sender, RoutedEventArgs e) => _commands.RestartAsAdministrator();
    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => Browser.ToggleSidebar();
    private void UpdateBar_CloseButtonClick(InfoBar sender, object args) => _updates.Dismissed = true;

    private void ViewRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Available is { } release) Shell.OpenUrl(release.Url);
    }

    private void DrivesMenu_Opening(object sender, object e)
    {
        // Rebuilt on every open: drives come and go.
        DrivesMenu.Items.Clear();
        DrivesMenu.Items.Add(new MenuFlyoutItem { Text = Strings.ScanDrive, IsEnabled = false });
        DrivesMenu.Items.Add(new MenuFlyoutSeparator());
        foreach (var drive in DiskStore.FixedDrives())
        {
            string label;
            try { label = drive.VolumeLabel; }
            catch (IOException) { label = ""; }
            catch (UnauthorizedAccessException) { label = ""; }
            var item = new MenuFlyoutItem { Text = Strings.DriveItem(drive.Name, label), Icon = new FontIcon { Glyph = "" } };
            string root = drive.Name;
            item.Click += (_, _) => _commands.ScanDrive(root);
            DrivesMenu.Items.Add(item);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
