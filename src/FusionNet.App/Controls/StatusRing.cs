using System.Windows;
using System.Windows.Media;

namespace FusionNet.App.Controls;

/// <summary>Circular progress ring used for the Fusion Mode hero and quality meters.</summary>
public sealed class StatusRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(StatusRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(StatusRing),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(StatusRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(StatusRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StartAngleProperty = DependencyProperty.Register(
        nameof(StartAngle), typeof(double), typeof(StatusRing),
        new FrameworkPropertyMetadata(-90d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CapProperty = DependencyProperty.Register(
        nameof(Cap), typeof(PenLineCap), typeof(StatusRing),
        new FrameworkPropertyMetadata(PenLineCap.Round, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ProgressBrush
    {
        get => (Brush?)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public double StartAngle
    {
        get => (double)GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public PenLineCap Cap
    {
        get => (PenLineCap)GetValue(CapProperty);
        set => SetValue(CapProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        var thickness = Math.Max(1, Thickness);
        if (size <= thickness * 2.1) return;

        var radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = TrackBrush ?? new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        drawingContext.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);

        var progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0.0005) return;

        var brush = ProgressBrush ?? new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
        var pen = new Pen(brush, thickness) { StartLineCap = Cap, EndLineCap = Cap };

        if (progress >= 0.999)
        {
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var geometry = BuildArc(center, radius, StartAngle, 360 * progress);
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Geometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0,
                isLargeArc: sweepAngle > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
