using System.Windows;
using System.Windows.Media;

namespace InterviewAgent.App.Controls;

public sealed class AudioWaveView : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(
            nameof(Level),
            typeof(double),
            typeof(AudioWaveView),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(AudioWaveView),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush WaveBrush = CreateWaveBrush();

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (!IsActive || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var level = Math.Clamp(Level, 0, 1);
        if (level <= 0.001)
        {
            return;
        }

        var width = ActualWidth;
        var height = ActualHeight;
        var waveLine = height * (1 - level);
        var amplitude = Math.Min(height * 0.07, 5) * (0.45 + (level * 0.55));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, waveLine), isFilled: true, isClosed: true);
            context.BezierTo(
                new Point(width * 0.17, waveLine - amplitude),
                new Point(width * 0.33, waveLine - amplitude),
                new Point(width * 0.5, waveLine),
                isStroked: true,
                isSmoothJoin: true);
            context.BezierTo(
                new Point(width * 0.67, waveLine + amplitude),
                new Point(width * 0.83, waveLine + amplitude),
                new Point(width, waveLine),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(new Point(width, height), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(0, height), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        drawingContext.PushClip(
            new EllipseGeometry(new Rect(0, 0, width, height)));
        drawingContext.DrawGeometry(WaveBrush, null, geometry);
        drawingContext.Pop();
    }

    private static Brush CreateWaveBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(190, 0x58, 0xAE, 0xDD));
        brush.Freeze();
        return brush;
    }
}
