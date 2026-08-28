using System.Windows;
using System.Windows.Media;

namespace CmdletForge.Controls;

public sealed class ScanlineOverlay : FrameworkElement
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ScanlineOverlay),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty ScanlineBrushProperty = DependencyProperty.Register(
        nameof(ScanlineBrush), typeof(Brush), typeof(ScanlineOverlay),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush ScanlineBrush
    {
        get => (Brush)GetValue(ScanlineBrushProperty);
        set => SetValue(ScanlineBrushProperty, value);
    }

    public static readonly DependencyProperty PhosphorBrushProperty = DependencyProperty.Register(
        nameof(PhosphorBrush), typeof(Brush), typeof(ScanlineOverlay),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush PhosphorBrush
    {
        get => (Brush)GetValue(PhosphorBrushProperty);
        set => SetValue(PhosphorBrushProperty, value);
    }

    public ScanlineOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!IsActive || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var scanline = new Pen(ScanlineBrush, 1);
        var phosphor = new Pen(PhosphorBrush, 1);
        scanline.Freeze();
        phosphor.Freeze();

        for (double y = 3.5; y < ActualHeight; y += 4)
        {
            drawingContext.DrawLine(scanline, new Point(0, y), new Point(ActualWidth, y));
        }

        for (double y = 4.5; y < ActualHeight; y += 12)
        {
            drawingContext.DrawLine(phosphor, new Point(0, y), new Point(ActualWidth, y));
        }
    }
}
