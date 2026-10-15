using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DiskU.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace DiskU.App;

/// <summary>A breadcrumb: a folder on the trail from the scan root to the display root.</summary>
public sealed class Crumb(FileNode node, bool current) : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public FileNode Node { get; } = node;
    public string Name => Node.Name;
    public override string ToString() => Name;
    /// <summary>Mac: the current crumb gets a faint rounded background.</summary>
    public Visibility CurrentVisibility => current ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The browser (Mac: BrowserView): outline sidebar, treemap pane with breadcrumbs and status bar, and the
/// inspector. Everything refreshes from <see cref="DiskStore"/> change notifications on the UI thread.
/// </summary>
public sealed partial class BrowserView : UserControl
{
    private const double SidebarMin = 240, SidebarMax = 480, InspectorMin = 250, InspectorMax = 400, TreemapMin = 360;

    private DiskStore _store = null!;
    private AppCommands _commands = null!;
    private TreemapView? _treemap;
    private ObservableCollection<OutlineItem> _rows = [];
    private bool _outlineQueued;
    private bool _scrollToSelection;
    private bool _syncingSelection;
    private bool _sidebarVisible = true;
    private double _sidebarWidth = 300;
    private double _inspectorWidth = 290;
    private FileNode? _copiedFor;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _copiedTimer;
    private FileNode? _shownDetails;
    private int _shownRevision = -1;

    public BrowserView()
    {
        InitializeComponent();
        _copiedTimer = DispatcherQueue.CreateTimer();
        _copiedTimer.Interval = TimeSpan.FromSeconds(1.5);
        _copiedTimer.IsRepeating = false;
        _copiedTimer.Tick += (_, _) => { _copiedFor = null; UpdateCopyButton(); };
        SidebarSplitter.Dragged += dx => SetSidebarWidth(SidebarColumn.ActualWidth + dx);
        InspectorSplitter.Dragged += dx => { _inspectorWidth = Math.Clamp(InspectorColumn.ActualWidth - dx, InspectorMin, InspectorMax); FitColumns(); };
        Columns.SizeChanged += (_, _) => FitColumns();
        Outline.ItemsSource = _rows;
    }

    internal void Initialize(DiskStore store, AppCommands commands)
    {
        _store = store;
        _commands = commands;
        _treemap = new TreemapView(store, commands.ItemMenu);
        TreemapHost.Children.Add(_treemap);
        _store.PropertyChanged += OnStoreChanged;

        RestartAdminButton.Content = Strings.RestartAsAdministrator;
        RestartAdminButton.Visibility = Shell.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        StatusHint.Text = Strings.StatusHint;
        EmptyText.Text = Strings.SelectAnItem;
        LegendTitle.Text = Strings.ByKind;
        RevealButton.Content = Label("", Strings.Reveal);
        ToolTipService.SetToolTip(RevealButton, Strings.ShowInExplorer);
        OpenButton.Content = Label("", Strings.Open);
        TrashButton.Content = Label("", Strings.MoveToRecycleBinEllipsis);
        UpdateCopyButton();
        RefreshAll();
    }

    /// <summary>Title bar toggle (Mac: the NavigationSplitView sidebar button).</summary>
    public void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        SidebarColumn.MinWidth = 0;
        FitColumns();
        Outline.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarSplitter.Visibility = Outline.Visibility;
    }

    internal TreemapView? Treemap => _treemap;

    private void SetSidebarWidth(double width)
    {
        _sidebarWidth = Math.Clamp(width, SidebarMin, SidebarMax);
        FitColumns();
    }

    /// <summary>
    /// Applies the preferred sidebar and inspector widths, shrinking both toward their minimums (inspector
    /// first) when the window is too narrow to leave the treemap <see cref="TreemapMin"/> wide.
    /// </summary>
    private void FitColumns()
    {
        double sidebar = _sidebarVisible ? _sidebarWidth : 0, inspector = _inspectorWidth;
        double excess = sidebar + inspector + TreemapMin + 12 - Columns.ActualWidth;
        if (Columns.ActualWidth > 0 && excess > 0)
        {
            double fromInspector = Math.Min(excess, inspector - InspectorMin);
            inspector -= fromInspector;
            if (_sidebarVisible) sidebar -= Math.Min(excess - fromInspector, sidebar - SidebarMin);
        }
        SidebarColumn.Width = new GridLength(sidebar);
        InspectorColumn.Width = new GridLength(inspector);
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Hidden unless a scan is done; skip the 10 Hz progress notifications.
        if (_store.State is not ScanState.Done) return;
        switch (e.PropertyName)
        {
            case nameof(DiskStore.State):
                if (_store.State is ScanState.Done) RefreshAll();
                break;
            case nameof(DiskStore.Revision):
                QueueOutline();
                UpdateDetails();
                UpdateLegend();
                UpdateCrumbs();
                UpdateStatus();
                break;
            case nameof(DiskStore.Expanded):
            case nameof(DiskStore.OutlineLimits):
                QueueOutline();
                break;
            case nameof(DiskStore.Selection):
                if (!_syncingSelection) _scrollToSelection = true;
                QueueOutline();
                UpdateDetails();
                UpdateStatus();
                break;
            case nameof(DiskStore.Hovered):
                UpdateStatus();
                break;
            case nameof(DiskStore.DisplayRoot):
                UpdateCrumbs();
                UpdateStatus();
                break;
        }
    }

    private void RefreshAll()
    {
        if (_store.State is not ScanState.Done done) return;
        InaccessibleBar.Title = Strings.FoldersCouldNotBeRead(done.Inaccessible);
        InaccessibleBar.Message = Strings.SizesIncomplete;
        InaccessibleBar.IsOpen = done.Inaccessible > 0;
        // A new tree: replace the rows outright rather than diffing against the old scan.
        _rows = [];
        Outline.ItemsSource = _rows;
        QueueOutline();
        UpdateDetails();
        UpdateLegend();
        UpdateCrumbs();
        UpdateStatus();
    }

    // ---- Outline ----

    /// <summary>Coalesces bursts of expansion, limit and selection changes into one rebuild.</summary>
    private void QueueOutline()
    {
        if (_outlineQueued) return;
        _outlineQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _outlineQueued = false;
            RebuildOutline();
        });
    }

    private void RebuildOutline()
    {
        var root = _store.Root;
        int revision = _store.Revision;
        var fresh = _store.OutlineRows().Select(r => new OutlineItem(r, revision, root)).ToList();

        // Keep unchanged rows (and their containers) by trimming the common prefix and suffix.
        int prefix = 0;
        while (prefix < _rows.Count && prefix < fresh.Count && _rows[prefix].SameAs(fresh[prefix])) prefix++;
        int suffix = 0;
        while (suffix < _rows.Count - prefix && suffix < fresh.Count - prefix && _rows[_rows.Count - 1 - suffix].SameAs(fresh[fresh.Count - 1 - suffix])) suffix++;
        int removed = _rows.Count - prefix - suffix, inserted = fresh.Count - prefix - suffix;

        _syncingSelection = true;
        try
        {
            if (removed + inserted > 1500)
            {
                _rows = new ObservableCollection<OutlineItem>(fresh);
                Outline.ItemsSource = _rows;
            }
            else
            {
                for (int i = 0; i < removed; i++) _rows.RemoveAt(prefix);
                for (int i = 0; i < inserted; i++) _rows.Insert(prefix + i, fresh[prefix + i]);
            }
            SyncOutlineSelection();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void SyncOutlineSelection()
    {
        var selection = _store.Selection;
        int index = -1;
        if (selection is not null)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (!_rows[i].IsShowMore && ReferenceEquals(_rows[i].Node, selection)) { index = i; break; }
        }
        if (Outline.SelectedIndex != index) Outline.SelectedIndex = index;
        if (index >= 0 && _scrollToSelection) Outline.ScrollIntoView(_rows[index]);
        _scrollToSelection = false;
    }

    private void Outline_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (Outline.SelectedItem is OutlineItem { IsShowMore: false } item)
        {
            _syncingSelection = true;
            try { _store.Selection = item.Node; }
            finally { _syncingSelection = false; }
        }
        else if (Outline.SelectedItem is OutlineItem)
        {
            // The show-more row isn't selectable: put the selection back.
            _syncingSelection = true;
            try { SyncOutlineSelection(); }
            finally { _syncingSelection = false; }
        }
    }

    private static OutlineItem? ItemFrom(object? source) => source switch
    {
        ListViewItem container => container.Content as OutlineItem,
        FrameworkElement element => element.DataContext as OutlineItem,
        _ => null,
    };

    private void Chevron_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        e.Handled = true; // toggling doesn't select, like the Mac's disclosure triangle
        _store.SetExpanded(item.Node, !item.Row.IsExpanded);
    }

    private void Outline_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ItemFrom(e.OriginalSource) is { IsShowMore: false, Row.IsExpandable: true } item)
            _store.SetExpanded(item.Node, !item.Row.IsExpanded);
    }

    private void Outline_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Outline.SelectedItem is not OutlineItem { IsShowMore: false } item) return;
        switch (e.Key)
        {
            case VirtualKey.Right when item.Row.IsExpandable && !item.Row.IsExpanded:
                _store.SetExpanded(item.Node, true);
                e.Handled = true;
                break;
            case VirtualKey.Left when item.Row.IsExpanded:
                _store.SetExpanded(item.Node, false);
                e.Handled = true;
                break;
            case VirtualKey.Left when item.Node.Parent is { } parent && _store.Root is { } root && item.Node != root:
                _store.Selection = parent;
                e.Handled = true;
                break;
        }
    }

    private void Outline_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var item = ItemFrom(e.OriginalSource) ?? Outline.SelectedItem as OutlineItem;
        if (item is null || item.IsShowMore) return;
        e.Handled = true;
        var menu = _commands.ItemMenu(item.Node);
        if (e.TryGetPosition(Outline, out var point))
            menu.ShowAt(Outline, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else if (Outline.ContainerFromItem(item) is FrameworkElement container)
            menu.ShowAt(container);
    }

    private void ShowMore_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { IsShowMore: true } item) _store.ShowMore(item.Node);
    }

    // ---- Breadcrumbs ----

    private void UpdateCrumbs()
    {
        var trail = _store.Breadcrumbs;
        var crumbs = trail.Select((n, i) => new Crumb(n, i == trail.Count - 1)).ToList();
        Crumbs.ItemsSource = crumbs;
        int pixels = (int)Math.Ceiling(16 * (XamlRoot?.RasterizationScale ?? 1));
        foreach (var crumb in crumbs) _ = LoadIconAsync(crumb, pixels);
        ZoomSize.Text = _store.ZoomRoot is { } zoom ? Format.Bytes(zoom.Size) : "";
        ZoomSize.Visibility = _store.ZoomRoot is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static async Task LoadIconAsync(Crumb crumb, int pixels) => crumb.Icon = await Icons.ForPathAsync(crumb.Node.Path, pixels);

    private void Crumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is Crumb crumb) _store.ZoomTo(crumb.Node);
    }

    // ---- Status bar ----

    private void UpdateStatus()
    {
        var node = _store.Hovered ?? _store.Selection;
        if (node is not null)
        {
            StatusDot.Fill = Ui.KindBrush(node.Kind);
            StatusDot.Visibility = Visibility.Visible;
            StatusPath.Text = node.Path;
            StatusPath.Visibility = Visibility.Visible;
            StatusHint.Visibility = Visibility.Collapsed;
            StatusRight.Text = Format.Bytes(node.Size);
        }
        else
        {
            StatusDot.Visibility = Visibility.Collapsed;
            StatusPath.Visibility = Visibility.Collapsed;
            StatusHint.Visibility = Visibility.Visible;
            StatusRight.Text = _store.DisplayRoot is { } display ? Strings.StatusTotals(display) : "";
        }
    }

    // ---- Inspector ----

    private void UpdateDetails()
    {
        var node = _store.Selection;
        Details.Visibility = node is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = node is null ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(node, _copiedFor)) { _copiedFor = null; _copiedTimer.Stop(); }
        if (node is null)
        {
            _shownDetails = null;
            return;
        }
        bool newNode = !ReferenceEquals(node, _shownDetails);
        if (!newNode && _shownRevision == _store.Revision) return;
        _shownDetails = node;
        _shownRevision = _store.Revision;

        DetailName.Text = node.Name;
        DetailKind.Text = node.Kind.Label();
        DetailKindDot.Fill = Ui.KindBrush(node.Kind);
        DetailSize.Text = Format.Bytes(node.Size);
        if (newNode)
        {
            DetailIcon.Source = null;
            _ = LoadDetailIconAsync(node);
        }

        Facts.Children.Clear();
        Facts.RowDefinitions.Clear();
        if (node.Kind is Kind.Folder or Kind.Bundle) AddFact(Strings.Contains, Format.Items(node.ItemCount));
        if (node.Parent is { Size: > 0 } parent) AddFact(Strings.Share, Strings.ShareOf((double)node.Size / parent.Size, parent.Name));
        if (Modified(node.Path) is { } modified) AddFact(Strings.Modified, Format.Date(modified));
        AddFact(Strings.Where, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(node.Path)) ?? node.Path);

        ToolTipService.SetToolTip(CopyButton, node.Path);
        TrashButton.IsEnabled = _store.CanTrash(node);
        UpdateCopyButton();
    }

    private async Task LoadDetailIconAsync(FileNode node)
    {
        int pixels = (int)Math.Ceiling(64 * (XamlRoot?.RasterizationScale ?? 1));
        var icon = await Icons.ForPathAsync(node.Path, pixels);
        if (ReferenceEquals(_store.Selection, node)) DetailIcon.Source = icon;
    }

    private static DateTime? Modified(string path)
    {
        try
        {
            if (File.Exists(path)) return File.GetLastWriteTime(path);
            if (Directory.Exists(path)) return Directory.GetLastWriteTime(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private void AddFact(string label, string value)
    {
        int row = Facts.RowDefinitions.Count;
        Facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = Ui.AppStyle("SecondaryCaptionStyle"),
        };
        // Mac: selectable, up to 3 lines, shortened in the middle so the end of a path stays visible.
        var text = new MiddleTrimmedText { Text = value, FontSize = 12, MaxLines = 3, IsTextSelectionEnabled = true };
        Grid.SetRow(title, row);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        Facts.Children.Add(title);
        Facts.Children.Add(text);
    }

    private void UpdateCopyButton()
    {
        bool copied = _copiedFor is not null && ReferenceEquals(_copiedFor, _store?.Selection);
        CopyButton.Content = copied ? Label("", Strings.Copied) : Label("", Strings.CopyPath);
    }

    private void UpdateLegend()
    {
        Legend.Children.Clear();
        var totals = _store.KindTotals;
        long largest = totals.Count > 0 ? totals[0].Bytes : 1;
        foreach (var (kind, bytes) in totals)
        {
            var header = new Grid { ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Ui.KindBrush(kind), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = kind.Label(), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var size = new TextBlock { Text = Format.Bytes(bytes), Style = Ui.AppStyle("SecondaryCaptionStyle"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(size, 2);
            header.Children.Add(swatch);
            header.Children.Add(label);
            header.Children.Add(size);
            var entry = new StackPanel { Spacing = 4 };
            entry.Children.Add(header);
            entry.Children.Add(Bar((double)bytes / largest, Ui.KindBrush(kind)));
            Legend.Children.Add(entry);
        }
    }

    /// <summary>Mac ProportionBar: 3 high, kind color over a faint track, at least 2 wide.</summary>
    private static Grid Bar(double fraction, Brush fill)
    {
        var track = new Grid { Style = Ui.AppStyle("ProportionTrackStyle") };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star), MinWidth = 2 });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0), GridUnitType.Star) });
        track.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = fill });
        return track;
    }

    private static StackPanel Label(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = text });
        return panel;
    }

    private void Reveal_Click(object sender, RoutedEventArgs e) => _commands.Reveal(_store.Selection);
    private void Open_Click(object sender, RoutedEventArgs e) => _commands.Open(_store.Selection);
    private void Trash_Click(object sender, RoutedEventArgs e) => _commands.RequestTrash(_store.Selection);
    private void RestartAdmin_Click(object sender, RoutedEventArgs e) => _commands.RestartAsAdministrator();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        _commands.CopyPath(_store.Selection);
        _copiedFor = _store.Selection;
        UpdateCopyButton();
        _copiedTimer.Stop();
        _copiedTimer.Start();
    }
}
