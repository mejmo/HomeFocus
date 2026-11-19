using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HomeFocus.App.Services;

namespace HomeFocus.App.Controls;

public sealed record TimelineItem(double StartHour, double EndHour, Brush? Fill, string Tip);

public sealed record BarPart(Brush Fill, double Minutes);

public sealed record BarItem(string Label, IReadOnlyList<BarPart> Parts, string Tip)
{
    public double Total => Parts.Sum(p => p.Minutes);
}

public sealed record ShareItem(Brush Fill, double Value);

/// <summary>Base for the hand-drawn charts: theme-aware brushes, text and hover tool tips.</summary>
public abstract class ChartBase : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(ChartBase),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Relative };

    protected ChartBase()
    {
        _tip.PlacementTarget = this;
        Loaded += (_, _) => ThemeManager.Changed += OnTheme;
        Unloaded += (_, _) => ThemeManager.Changed -= OnTheme;
    }

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    private void OnTheme(object? s, EventArgs e) => InvalidateVisual();

    protected Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    protected FormattedText Text(string s, double size, Brush brush, FontWeight? weight = null) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Tip text for the point under the mouse, or null.</summary>
    protected abstract string? HitTip(Point p);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var tip = HitTip(p);
        if (tip is null)
        {
            _tip.IsOpen = false;
            return;
        }
        _tip.Content = tip;
        _tip.HorizontalOffset = p.X + 14;
        _tip.VerticalOffset = p.Y + 18;
        _tip.IsOpen = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e) => _tip.IsOpen = false;

    protected static Brush Hatch(Brush a, Brush b)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(b, null, new RectangleGeometry(new Rect(0, 0, 6, 6))));
        g.Children.Add(new GeometryDrawing(null, new Pen(a, 1.6), new LineGeometry(new Point(-1, 7), new Point(7, -1))));
        g.Children.Add(new GeometryDrawing(null, new Pen(a, 1.6), new LineGeometry(new Point(5, 7), new Point(7, 5))));
        g.Children.Add(new GeometryDrawing(null, new Pen(a, 1.6), new LineGeometry(new Point(-1, 1), new Point(1, -1))));
        var brush = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 6, 6), ViewportUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 6, 6), ViewboxUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }
}

/// <summary>Horizontal day timeline: colored app segments, hatched idle time and a "now" marker.</summary>
public sealed class TimelineBar : ChartBase
{
    public static readonly DependencyProperty StartHourProperty = DependencyProperty.Register(
        nameof(StartHour), typeof(double), typeof(TimelineBar), new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NowHourProperty = DependencyProperty.Register(
        nameof(NowHour), typeof(double?), typeof(TimelineBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double BarH = 44, LabelH = 22;

    public TimelineBar() => Height = BarH + LabelH;

    public double StartHour { get => (double)GetValue(StartHourProperty); set => SetValue(StartHourProperty, value); }
    public double? NowHour { get => (double?)GetValue(NowHourProperty); set => SetValue(NowHourProperty, value); }

    private double X(double hour) => (hour - StartHour) / (24 - StartHour) * ActualWidth;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        if (w <= 0) return;
        var track = new RectangleGeometry(new Rect(0, 0, w, BarH), 8, 8);
        dc.DrawGeometry(Res("Brush.Subtle"), null, track);
        dc.PushClip(track);
        var idle = Hatch(Res("Brush.IdleA"), Res("Brush.IdleB"));
        foreach (var it in Items?.OfType<TimelineItem>() ?? [])
        {
            var x0 = X(Math.Max(it.StartHour, StartHour));
            var x1 = X(it.EndHour);
            if (x1 <= x0) continue;
            dc.DrawRectangle(it.Fill ?? idle, null, new Rect(x0, 0, Math.Max(1, x1 - x0), BarH));
        }
        dc.Pop();

        if (NowHour is { } now && now >= StartHour)
            dc.DrawRoundedRectangle(Res("Brush.Text"), null, new Rect(X(now) - 1, -4, 2, BarH + 8), 1, 1);

        var step = (24 - StartHour) > 12 ? 3 : 2;
        var muted = Res("Brush.Muted");
        for (var h = (int)Math.Ceiling(StartHour); h <= 24; h++)
        {
            if ((h - (int)Math.Ceiling(StartHour)) % step != 0 && h != 24) continue;
            var t = Text($"{h:00}:00", 11.5, muted);
            var x = Math.Clamp(X(h) - t.Width / 2, 0, w - t.Width);
            dc.DrawText(t, new Point(x, BarH + 6));
        }
    }

    protected override string? HitTip(Point p)
    {
        if (p.Y > BarH || ActualWidth <= 0) return null;
        var hour = StartHour + p.X / ActualWidth * (24 - StartHour);
        return Items?.OfType<TimelineItem>().LastOrDefault(i => i.StartHour <= hour && i.EndHour >= hour)?.Tip;
    }
}

/// <summary>Vertical stacked bars per day with an hour axis.</summary>
public sealed class StackedBarChart : ChartBase
{
    private const double AxisW = 36, LabelH = 24;
    private readonly List<(Rect Rect, string Tip)> _hits = [];

    public StackedBarChart() => Height = 240;

    protected override void OnRender(DrawingContext dc)
    {
        _hits.Clear();
        var items = Items?.OfType<BarItem>().ToList() ?? [];
        var w = ActualWidth - AxisW;
        var h = ActualHeight - LabelH;
        if (w <= 0 || h <= 0) return;

        var maxH = Math.Max(2, Math.Ceiling(items.Select(i => i.Total).DefaultIfEmpty(0).Max() / 60 / 2) * 2);
        var muted = Res("Brush.Muted");
        var grid = new Pen(Res("Brush.Border"), 1);
        for (var k = 0; k <= 4; k++)
        {
            var y = Math.Round(h - h * k / 4) + 0.5;
            dc.DrawLine(grid, new Point(AxisW, y), new Point(ActualWidth, y));
            var t = Text(k == 0 ? "0" : $"{maxH * k / 4:0.#} h", 11, muted);
            dc.DrawText(t, new Point(AxisW - 8 - t.Width, y - t.Height / 2));
        }
        if (items.Count == 0) return;

        var slot = w / items.Count;
        var barW = Math.Min(48, slot * (items.Count > 14 ? 0.7 : 0.55));
        var labelEvery = items.Count > 14 ? (int)Math.Ceiling(items.Count / 15.0) : 1;
        for (var i = 0; i < items.Count; i++)
        {
            var x = AxisW + slot * i + (slot - barW) / 2;
            var total = items[i].Total;
            var barH = total / 60 / maxH * h;
            if (barH > 0.5)
            {
                var clip = new RectangleGeometry(new Rect(x, h - barH, barW, barH + 6), Math.Min(5, barW / 3), Math.Min(5, barW / 3));
                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Intersect, clip, new RectangleGeometry(new Rect(x, 0, barW, h))));
                var y = h;
                foreach (var part in items[i].Parts)
                {
                    var ph = part.Minutes / 60 / maxH * h;
                    if (ph <= 0) continue;
                    dc.DrawRectangle(part.Fill, null, new Rect(x, y - ph, barW, ph + 0.5));
                    y -= ph;
                }
                dc.Pop();
            }
            _hits.Add((new Rect(AxisW + slot * i, 0, slot, h), items[i].Tip));
            if (i % labelEvery == 0)
            {
                var t = Text(items[i].Label, 11.5, muted);
                dc.DrawText(t, new Point(AxisW + slot * i + (slot - t.Width) / 2, h + 7));
            }
        }
    }

    protected override string? HitTip(Point p) => _hits.FirstOrDefault(hh => hh.Rect.Contains(p)).Tip;
}

/// <summary>One horizontal bar split into proportional colored parts.</summary>
public sealed class ProportionBar : ChartBase
{
    public ProportionBar() => Height = 12;

    protected override void OnRender(DrawingContext dc)
    {
        var items = Items?.OfType<ShareItem>().Where(i => i.Value > 0).ToList() ?? [];
        var w = ActualWidth;
        var h = ActualHeight;
        var shape = new RectangleGeometry(new Rect(0, 0, w, h), h / 2, h / 2);
        dc.DrawGeometry(Res("Brush.Track"), null, shape);
        var sum = items.Sum(i => i.Value);
        if (sum <= 0 || w <= 0) return;
        dc.PushClip(shape);
        var gap = 2.0;
        var usable = w - gap * (items.Count - 1);
        var x = 0.0;
        foreach (var it in items)
        {
            var pw = usable * it.Value / sum;
            dc.DrawRectangle(it.Fill, null, new Rect(x, 0, pw, h));
            x += pw + gap;
        }
        dc.Pop();
    }

    protected override string? HitTip(Point p) => null;
}
