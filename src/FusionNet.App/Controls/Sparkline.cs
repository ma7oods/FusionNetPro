using System.Windows;
using System.Windows.Media;

namespace FusionNet.App.Controls;

/// <summary>Lightweight filled line chart, used for ping latency and throughput history.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(1.8d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(Sparkline),
        new FrameworkPropertyMetadata(new Thickness(1), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
        nameof(MinimumRange), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public double MinimumRange
    {
        get => (double)GetValue(MinimumRangeProperty);
        set => SetValue(MinimumRangeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var values = Values?.ToArray() ?? [];
        var width = ActualWidth - Padding.Left - Padding.Right;
        var height = ActualHeight - Padding.Top - Padding.Bottom;
        if (values.Length == 0 || width <= 2 || height <= 2) return;

        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(max - min, Math.Max(MinimumRange, 0.001));
        var mid = (min + max) / 2;
        min = mid - range / 2;
        max = mid + range / 2;

        var points = new Point[values.Length];
        var step = values.Length == 1 ? 0 : width / (values.Length - 1);

        for (var i = 0; i < values.Length; i++)
        {
            var normalized = (values[i] - min) / (max - min);
            normalized = double.IsFinite(normalized) ? Math.Clamp(normalized, 0, 1) : 0.5;
            points[i] = new Point(
                Padding.Left + (values.Length == 1 ? width / 2 : i * step),
                Padding.Top + height - normalized * height);
        }

        var lineGeometry = new StreamGeometry();
        using (var context = lineGeometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            if (points.Length == 1)
            {
                context.LineTo(new Point(points[0].X + 0.1, points[0].Y), true, false);
            }
            else
            {
                context.PolyLineTo(points.Skip(1).ToArray(), isStroked: true, isSmoothJoin: true);
            }
        }
        lineGeometry.Freeze();

        var fillBrush = Fill;
        if (fillBrush is not null && points.Length > 1)
        {
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                context.BeginFigure(new Point(points[0].X, Padding.Top + height), isFilled: true, isClosed: true);
                context.LineTo(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToArray(), true, true);
                context.LineTo(new Point(points[^1].X, Padding.Top + height), false, false);
            }
            area.Freeze();
            drawingContext.DrawGeometry(fillBrush, null, area);
        }

        var pen = new Pen(Stroke ?? Brushes.Cyan, StrokeThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze) pen.Freeze();
        drawingContext.DrawGeometry(null, pen, lineGeometry);
    }
}
