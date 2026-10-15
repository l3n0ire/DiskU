using DiskU.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace DiskU.App;

/// <summary>Colors and brushes shared by every view, from <see cref="Kinds"/> and <see cref="Palette"/>.</summary>
internal static class Ui
{
    private static readonly SolidColorBrush[] KindBrushes = Kinds.All.Select(k => new SolidColorBrush(ToColor(k.Color()))).ToArray();

    public static Color ToColor(Rgb rgb, byte alpha = 255) => Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);

    /// <summary>One shared brush per kind, so rows don't allocate.</summary>
    public static SolidColorBrush KindBrush(Kind kind) => KindBrushes[(int)kind];

    /// <summary>A style from App.xaml. Code-built elements take theme colors through styles so they follow theme changes.</summary>
    public static Style AppStyle(string key) => (Style)Application.Current.Resources[key];
}

/// <summary>
/// Text (one line, or up to <see cref="MaxLines"/> wrapped lines) that shortens in the middle ("C:\Users\me\…\file.txt") to fit its width, like SwiftUI's
/// truncationMode(.middle). WinUI only trims at the end. Font and color inherit from this control.
/// </summary>
public sealed partial class MiddleTrimmedText : UserControl
{
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap };
    private string _full = "";
    private int _maxLines = 1;

    public MiddleTrimmedText()
    {
        Content = _text;
        IsTabStop = false;
    }

    public string Text
    {
        get => _full;
        set
        {
            if (_full == value) return;
            _full = value ?? "";
            ToolTipService.SetToolTip(this, null);
            InvalidateMeasure();
        }
    }

    /// <summary>Wrap onto at most this many lines before shortening (default 1: no wrapping).</summary>
    public int MaxLines
    {
        get => _maxLines;
        set
        {
            _maxLines = Math.Max(1, value);
            _text.TextWrapping = _maxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
            InvalidateMeasure();
        }
    }

    /// <summary>Lets the user select and copy the text.</summary>
    public bool IsTextSelectionEnabled
    {
        get => _text.IsTextSelectionEnabled;
        set => _text.IsTextSelectionEnabled = value;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        string shown = Fit(availableSize.Width);
        if (_text.Text != shown) _text.Text = shown;
        _text.Measure(new Size(_maxLines > 1 ? availableSize.Width : double.PositiveInfinity, availableSize.Height));
        // Show the whole text on hover when it had to be shortened.
        ToolTipService.SetToolTip(this, shown.Length < _full.Length ? _full : null);
        return new Size(Math.Min(_text.DesiredSize.Width, availableSize.Width), _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _text.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    private bool Fits(string text, double width)
    {
        _text.Text = text;
        if (_maxLines == 1)
        {
            _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _text.DesiredSize.Width <= width;
        }
        _text.Measure(new Size(width, double.PositiveInfinity));
        double height = _text.DesiredSize.Height;
        _text.Text = "X";
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return height <= _text.DesiredSize.Height * _maxLines + 0.5;
    }

    private string Fit(double width)
    {
        if (double.IsInfinity(width) || _full.Length < 4 || Fits(_full, width)) return _full;
        // Binary search the longest "front…back" that fits; the front gets the extra character.
        int low = 0, high = _full.Length - 1;
        string best = "…";
        while (low <= high)
        {
            int keep = (low + high) / 2;
            string candidate = Shorten(keep);
            if (Fits(candidate, width))
            {
                best = candidate;
                low = keep + 1;
            }
            else
            {
                high = keep - 1;
            }
        }
        return best;
    }

    private string Shorten(int keep)
    {
        int front = (keep + 1) / 2, back = keep - front;
        return string.Concat(_full.AsSpan(0, front), "…", _full.AsSpan(_full.Length - back, back));
    }
}

/// <summary>A vertical drag handle between columns. Reports horizontal drag deltas in DIPs.</summary>
public sealed partial class Splitter : Grid
{
    private double _startX;
    private bool _dragging;

    public Splitter()
    {
        Width = 6;
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)); // hit-testable, invisible
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        PointerPressed += (_, e) =>
        {
            _dragging = CapturePointer(e.Pointer);
            _startX = e.GetCurrentPoint(null).Position.X;
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            double x = e.GetCurrentPoint(null).Position.X;
            Dragged?.Invoke(x - _startX);
            _startX = x;
        };
        PointerReleased += (_, e) => { _dragging = false; ReleasePointerCapture(e.Pointer); };
        PointerCaptureLost += (_, _) => _dragging = false;
    }

    public event Action<double>? Dragged;
}
