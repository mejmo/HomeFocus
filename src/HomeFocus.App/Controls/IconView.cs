using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace HomeFocus.App.Controls;

/// <summary>Draws a stroke icon defined on a 24×24 grid, colored by the inherited Foreground.</summary>
public sealed class IconView : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(IconView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(IconView), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(IconView),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(IconView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IconView()
    {
        Width = 18;
        Height = 18;
        SnapsToDevicePixels = true;
    }

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is null || ActualWidth <= 0) return;
        var s = Math.Min(ActualWidth, ActualHeight) / 24.0;
        var pen = new Pen(Foreground, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * s) / 2, (ActualHeight - 24 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(Filled ? Foreground : null, pen, Data);
        dc.Pop();
        dc.Pop();
    }
}
