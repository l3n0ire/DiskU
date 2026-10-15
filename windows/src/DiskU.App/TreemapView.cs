using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using DiskU.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace DiskU.App;

/// <summary>
/// The treemap (Mac: TreemapView + TreemapCanvas + HighlightCanvas). Two Win2D canvases: the tile layer is
/// redrawn only when the layout changes (display root, size, or tree revision); the overlay draws the hover
/// and selection outlines and is the only thing redrawn while the pointer moves. Rendering follows
/// mac-spec section 2.9 exactly; the colors come from <see cref="Palette"/>.
/// </summary>
internal sealed partial class TreemapView : Grid
{
    private static readonly Color White = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color WhiteDetail = Color.FromArgb(204, 255, 255, 255);   // white 80%
    private static readonly Color HoverStroke = Color.FromArgb(179, 255, 255, 255);   // white 70%
    private static readonly Color SelectionHalo = Color.FromArgb(153, 0, 0, 0);       // black 60%
    private static readonly Color LabelShadow = Color.FromArgb(153, 0, 0, 0);         // black 60%
    private static readonly Color LeafStroke = Color.FromArgb((byte)Math.Round(Palette.LeafStrokeOpacity * 255), 0, 0, 0);
    private const string LabelFont = "Segoe UI Variable Small";

    private readonly CanvasControl _tiles = new();
    private readonly CanvasControl _overlay = new() { IsHitTestVisible = false };
    private readonly DiskStore _store;
    private readonly Func<FileNode, MenuFlyout> _menu;

    private TreemapLayout? _drawn;
    // The last full render, reused (stretched) while the window is being resized so dragging stays smooth
    // on huge trees; the real layout and render run once the size settles.
    private CanvasRenderTarget? _frame;
    private TreemapLayout? _frameLayout;
    private bool _resizing;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _settle;
    private Dictionary<FileNode, RectD> _rects = new(ReferenceEqualityComparer.Instance);
    private CanvasDevice? _resourceDevice;
    private CanvasLinearGradientBrush[] _cushions = [];
    private readonly CanvasTextFormat _leafTitle = Format(Palette.LeafTitleSize, 500);
    private readonly CanvasTextFormat _leafDetail = Format(Palette.LeafDetailSize, 400);
    private readonly CanvasTextFormat _headerTitle = Format(Palette.HeaderTitleSize, 600, CanvasVerticalAlignment.Center);
    private readonly CanvasTextFormat _headerDetail = Format(Palette.HeaderDetailSize, 400, CanvasVerticalAlignment.Center, CanvasHorizontalAlignment.Right);

    public TreemapView(DiskStore store, Func<FileNode, MenuFlyout> menu)
    {
        _store = store;
        _menu = menu;
        Background = new SolidColorBrush(Ui.ToColor(Palette.Background));
        _tiles.ClearColor = Ui.ToColor(Palette.Background);
        _overlay.ClearColor = Color.FromArgb(0, 0, 0, 0);
        Children.Add(_tiles);
        Children.Add(_overlay);
        _tiles.Draw += DrawTiles;
        _settle = DispatcherQueue.CreateTimer();
        _settle.Interval = TimeSpan.FromMilliseconds(120);
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) => { _resizing = false; _tiles.Invalidate(); _overlay.Invalidate(); };
        SizeChanged += (_, _) =>
        {
            if (_frame is null) return;
            _resizing = true;
            _overlay.Invalidate();
            _settle.Stop();
            _settle.Start();
        };
        _overlay.Draw += DrawOverlay;

        PointerMoved += (_, e) => _store.Hovered = TileAt(e.GetCurrentPoint(this).Position)?.Node;
        PointerExited += (_, _) => _store.Hovered = null;
        Tapped += (_, e) =>
        {
            if (TileAt(e.GetPosition(this)) is { } tile) _store.Selection = tile.Node;
        };
        DoubleTapped += (_, e) =>
        {
            if (TileAt(e.GetPosition(this)) is { } tile) _store.Zoom(tile.Node);
        };
        ContextRequested += OnContextRequested;
        _store.PropertyChanged += OnStoreChanged;
        Unloaded += (_, _) =>
        {
            _store.PropertyChanged -= OnStoreChanged;
            // Win2D: release the swap chains when the view goes away.
            _tiles.RemoveFromVisualTree();
            _overlay.RemoveFromVisualTree();
            _frame?.Dispose();
            _frame = null;
        };
    }

    /// <summary>Last frame's timings, for the perf log.</summary>
    public (double LayoutMs, double DrawMs, int Tiles) LastFrame { get; private set; }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DiskStore.Revision):
            case nameof(DiskStore.DisplayRoot):
                _tiles.Invalidate();
                _overlay.Invalidate();
                break;
            case nameof(DiskStore.Hovered):
            case nameof(DiskStore.Selection):
                _overlay.Invalidate();
                break;
        }
    }

    private TreemapTile? TileAt(Point p) => _drawn?.TileAt(new PointD(p.X, p.Y));

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        // Mac: the menu targets the tile under the mouse, falling back to the selection.
        bool atPointer = e.TryGetPosition(this, out var point);
        var target = (atPointer ? TileAt(point)?.Node : null) ?? _store.Hovered ?? _store.Selection;
        if (target is null) return;
        e.Handled = true;
        var flyout = _menu(target);
        if (atPointer)
        {
            flyout.ShowAt(this, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        }
        else
        {
            // Keyboard (Shift+F10 / Menu key): open at the item's tile, or the top-left corner.
            var rect = _rects.TryGetValue(target, out var r) ? r : new RectD(0, 0, 24, 24);
            flyout.ShowAt(this, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = new Point(rect.MidX, rect.MidY) });
        }
    }

    // ---- Tile layer ----

    private void DrawTiles(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_resizing && _frame is not null)
        {
            // Mid-resize: stretch the last frame; the settle timer redraws for real.
            args.DrawingSession.DrawImage(_frame, new Rect(0, 0, sender.Size.Width, sender.Size.Height), _frame.Bounds);
            return;
        }
        var watch = Stopwatch.StartNew();
        var layout = _store.Layout(sender.Size.Width, sender.Size.Height);
        double layoutMs = watch.Elapsed.TotalMilliseconds;
        if (!ReferenceEquals(layout, _drawn))
        {
            _drawn = layout;
            _rects = new Dictionary<FileNode, RectD>(ReferenceEqualityComparer.Instance);
            if (layout is not null)
                foreach (var tile in layout.Tiles)
                    if (!tile.IsRemainder) _rects.TryAdd(tile.Node, tile.Rect);
            _overlay.Invalidate();
        }
        if (layout is null) return;

        bool fresh = _frame is null || !ReferenceEquals(layout, _frameLayout) || !ReferenceEquals(_frame.Device, sender.Device)
            || Math.Abs(_frame.Size.Width - sender.Size.Width) > 0.5 || Math.Abs(_frame.Size.Height - sender.Size.Height) > 0.5;
        if (fresh)
        {
            _frame?.Dispose();
            _frame = new CanvasRenderTarget(sender, (float)sender.Size.Width, (float)sender.Size.Height);
            using (var frame = _frame.CreateDrawingSession())
            {
                frame.Clear(Ui.ToColor(Palette.Background));
                RenderTiles(frame, sender, layout);
            }
            _frameLayout = layout;
        }
        args.DrawingSession.DrawImage(_frame!);

        LastFrame = (layoutMs, watch.Elapsed.TotalMilliseconds - layoutMs, layout.Tiles.Count);
        if (Diagnostics.Enabled && fresh)
            Diagnostics.Log($"treemap {sender.Size.Width:0}x{sender.Size.Height:0} tiles={layout.Tiles.Count} layout={layoutMs:0.0}ms draw={LastFrame.DrawMs:0.0}ms");
    }

    /// <summary>Paints every tile and label of the layout (mac-spec 2.9).</summary>
    private void RenderTiles(CanvasDrawingSession ds, ICanvasResourceCreator creator, TreemapLayout layout)
    {
        EnsureResources(creator.Device);
        using var labels = new CanvasCommandList(creator);
        using (var text = labels.CreateDrawingSession())
        {
            foreach (var tile in layout.Tiles)
            {
                switch (tile.Role)
                {
                    case TileRole.Folder: DrawFolder(ds, text, tile); break;
                    case TileRole.Leaf: DrawLeaf(ds, text, tile); break;
                    default: ds.FillRectangle(R(tile.Rect), Ui.ToColor(Palette.Remainder)); break;
                }
            }
        }
        // Mac: one drop shadow on the whole label layer (black 60%, blur 1, offset 0/+0.5).
        using var shadow = new ShadowEffect { Source = labels, BlurAmount = 1, ShadowColor = LabelShadow };
        ds.DrawImage(shadow, 0, 0.5f);
        ds.DrawImage(labels);
    }

    private void DrawFolder(CanvasDrawingSession ds, CanvasDrawingSession text, TreemapTile tile)
    {
        ds.FillRectangle(R(tile.Rect), Ui.ToColor(Palette.FolderFill(tile.Depth)));
        if (tile.Header is not { } header) return;
        ds.FillRectangle(R(header), Ui.ToColor(Palette.HeaderFill(tile.Depth)));
        var label = header.Inset(4, 0);
        if (label.Width <= 0) return;
        var rect = R(label);
        string title = tile.Node.Name;
        text.DrawText(title, rect, White, _headerTitle);
        // The size goes on the right only when it can't collide with the title.
        if (TitleFits(title, label.Width - 60, text)) text.DrawText(Core.Format.Bytes(tile.Node.Size), rect, WhiteDetail, _headerDetail);
    }

    private bool TitleFits(string title, double width, CanvasDrawingSession session)
    {
        if (width <= 0) return false;
        // Cheap bounds first: 10.5 pt glyphs are at most ~11 DIPs wide and at least ~2.
        if (title.Length * 11.0 < width) return true;
        if (title.Length * 2.0 >= width) return false;
        using var layout = new CanvasTextLayout(session, title, _headerTitle, float.MaxValue, 15);
        return layout.LayoutBounds.Width < width;
    }

    private void DrawLeaf(CanvasDrawingSession ds, CanvasDrawingSession text, TreemapTile tile)
    {
        var r = tile.Rect;
        var kind = Palette.LeafKind(tile.Node);
        if (r.Width < Palette.SolidFillBelow || r.Height < Palette.SolidFillBelow)
        {
            ds.FillRectangle(R(r), kind is { } k ? Ui.ToColor(k.Color()) : Ui.ToColor(Palette.EmptyFolder));
            return;
        }
        var brush = _cushions[kind is { } kk ? (int)kk : _cushions.Length - 1];
        // The gradient runs in absolute coordinates from the top-left to the bottom-right corner.
        brush.StartPoint = new Vector2((float)r.MinX, (float)r.MinY);
        brush.EndPoint = new Vector2((float)r.MaxX, (float)r.MaxY);
        ds.FillRectangle(R(r), brush);
        ds.DrawRectangle(R(r.Inset(Palette.LeafStrokeInset, Palette.LeafStrokeInset)), LeafStroke, (float)Palette.LeafStrokeWidth);

        if (r.Width > Palette.LeafLabelMinWidth && r.Height > Palette.LeafLabelMinHeight)
        {
            var label = r.Inset(5, 4);
            text.DrawText(tile.Node.Name, R(label), White, _leafTitle);
            if (label.Height > 30)
            {
                // Detail top sits 14 DIPs below the title's top (title size + 3).
                var detail = new RectD(label.X, label.Y + Palette.LeafTitleSize + 3, label.Width, label.Height - Palette.LeafTitleSize - 3);
                text.DrawText(Core.Format.Bytes(tile.Node.Size), R(detail), WhiteDetail, _leafDetail);
            }
        }
    }

    private void EnsureResources(CanvasDevice device)
    {
        if (ReferenceEquals(device, _resourceDevice)) return;
        foreach (var old in _cushions) old.Dispose();
        var cushions = new List<CanvasLinearGradientBrush>();
        foreach (Kind? kind in Kinds.All.Select(k => (Kind?)k).Append(null))
        {
            var (highlight, mid, dark) = Palette.Cushion(kind);
            var stops = new[]
            {
                new CanvasGradientStop { Position = (float)Palette.CushionStops[0], Color = Ui.ToColor(highlight) },
                new CanvasGradientStop { Position = (float)Palette.CushionStops[1], Color = Ui.ToColor(mid) },
                new CanvasGradientStop { Position = (float)Palette.CushionStops[2], Color = Ui.ToColor(dark) },
            };
            cushions.Add(new CanvasLinearGradientBrush(device, stops));
        }
        _cushions = [.. cushions];
        _resourceDevice = device;
    }

    // ---- Overlay ----

    private void DrawOverlay(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_resizing) return; // outlines would not match the stretched frame
        var ds = args.DrawingSession;
        var hovered = _store.Hovered;
        var selection = _store.Selection;
        if (hovered is not null && !ReferenceEquals(hovered, selection) && _rects.TryGetValue(hovered, out var hover))
            ds.DrawRectangle(R(hover.Inset(0.5, 0.5)), HoverStroke, 1);
        if (selection is not null && _rects.TryGetValue(selection, out var selected))
        {
            var rect = R(selected.Inset(1.5, 1.5));
            ds.DrawRectangle(rect, SelectionHalo, 3);
            ds.DrawRectangle(rect, White, 1.5f);
        }
    }

    // ---- Helpers ----

    private static Rect R(RectD r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));

    private static CanvasTextFormat Format(double size, ushort weight,
        CanvasVerticalAlignment vertical = CanvasVerticalAlignment.Top,
        CanvasHorizontalAlignment horizontal = CanvasHorizontalAlignment.Left) => new()
        {
            FontFamily = LabelFont,
            FontSize = (float)size,
            FontWeight = new Windows.UI.Text.FontWeight(weight),
            WordWrapping = CanvasWordWrapping.NoWrap,
            // Hard clip, no ellipsis, as on the Mac.
            Options = CanvasDrawTextOptions.Clip,
            VerticalAlignment = vertical,
            HorizontalAlignment = horizontal,
        };
}
