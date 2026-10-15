using DiskU.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DiskU.App;

/// <summary>
/// One sidebar row for the virtualized ListView: a <see cref="OutlineRow"/> plus the revision it was built
/// at, so a row is rebuilt when sizes change. Every property is computed on demand and bound OneTime.
/// </summary>
public sealed class OutlineItem(OutlineRow row, int revision, FileNode? root)
{
    private const double IndentPerLevel = 14;

    public OutlineRow Row { get; } = row;
    public FileNode Node => Row.Node;
    public bool IsShowMore => Row.IsShowMore;

    public Thickness Indent => new(Row.Depth * IndentPerLevel, 0, 0, 0);
    public string Name => Node.Name;
    public string SizeText => Format.Bytes(Node.Size);
    public ImageSource? Icon => Icons.Outline(Node, root);
    public Visibility ChevronVisibility => Row.IsExpandable ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>ChevronDown when open, ChevronRight when closed (Segoe Fluent Icons).</summary>
    public string ChevronGlyph => Row.IsExpanded ? "" : "";
    public Brush KindBrush => Ui.KindBrush(Node.Kind);

    // Proportion bar: two star columns split by the share of the parent.
    public GridLength BarFilled => new(Math.Max(Row.Fraction, 0.0001), GridUnitType.Star);
    public GridLength BarEmpty => new(Math.Max(1 - Row.Fraction, 0), GridUnitType.Star);

    // Show-more rows.
    public string SmallerItems => Strings.SmallerItems(Row.HiddenCount);
    public string HiddenSize => Format.Bytes(Row.HiddenBytes);
    public string ShowMoreLabel => Strings.ShowMore;
    public string ShowMoreTip => Strings.ShowNext(Row.HiddenCount);

    private readonly int _revision = revision;

    /// <summary>The accessible name of the row (UI Automation reads ToString of a list item).</summary>
    public override string ToString() => IsShowMore ? $"{SmallerItems}, {HiddenSize}" : $"{Name}, {SizeText}";

    /// <summary>True when the row would look the same, so the ListView can keep its container.</summary>
    public bool SameAs(OutlineItem other) => _revision == other._revision && Row == other.Row;
}

/// <summary>Picks the item or show-more template.</summary>
public sealed partial class OutlineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ItemTemplate { get; set; }
    public DataTemplate? ShowMoreTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is OutlineItem { IsShowMore: true } ? ShowMoreTemplate : ItemTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
