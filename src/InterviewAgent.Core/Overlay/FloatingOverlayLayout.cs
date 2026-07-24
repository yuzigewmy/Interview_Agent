namespace InterviewAgent.Core.Overlay;

public enum OverlaySide
{
    Left,
    Right,
}

public readonly record struct OverlayPoint(double Left, double Top);

public readonly record struct OverlayBounds(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public static class FloatingOverlayLayout
{
    public const double DefaultDiameter = 72;
    public const double MinimumDiameter = 56;
    public const double MaximumDiameter = 128;
    public const double EdgeMargin = 8;
    public const double BubbleGap = 12;

    public static double NormalizeDiameter(double requested) =>
        double.IsFinite(requested)
            ? Math.Clamp(requested, MinimumDiameter, MaximumDiameter)
            : DefaultDiameter;

    public static OverlayPoint ClampBallToWorkArea(
        double left,
        double top,
        double diameter,
        OverlayBounds workArea)
    {
        var normalizedDiameter = NormalizeDiameter(diameter);
        var minimumLeft = workArea.Left + EdgeMargin;
        var maximumLeft = Math.Max(
            minimumLeft,
            workArea.Right - normalizedDiameter - EdgeMargin);
        var minimumTop = workArea.Top + EdgeMargin;
        var maximumTop = Math.Max(
            minimumTop,
            workArea.Bottom - normalizedDiameter - EdgeMargin);
        return new OverlayPoint(
            Math.Clamp(left, minimumLeft, maximumLeft),
            Math.Clamp(top, minimumTop, maximumTop));
    }

    public static OverlaySide ChooseBubbleSide(
        double ballLeft,
        double ballDiameter,
        double bubbleWidth,
        OverlayBounds workArea)
    {
        var leftSpace = ballLeft - BubbleGap - workArea.Left;
        var rightSpace =
            workArea.Right - (ballLeft + ballDiameter + BubbleGap);
        if (leftSpace >= bubbleWidth)
        {
            return OverlaySide.Left;
        }

        if (rightSpace >= bubbleWidth)
        {
            return OverlaySide.Right;
        }

        return leftSpace >= rightSpace
            ? OverlaySide.Left
            : OverlaySide.Right;
    }

    public static OverlayBounds PositionBubble(
        double ballLeft,
        double ballTop,
        double ballDiameter,
        double bubbleWidth,
        double bubbleHeight,
        OverlayBounds workArea)
    {
        var side = ChooseBubbleSide(
            ballLeft,
            ballDiameter,
            bubbleWidth,
            workArea);
        var requestedLeft = side == OverlaySide.Left
            ? ballLeft - BubbleGap - bubbleWidth
            : ballLeft + ballDiameter + BubbleGap;
        var minimumLeft = workArea.Left + EdgeMargin;
        var maximumLeft = Math.Max(
            minimumLeft,
            workArea.Right - bubbleWidth - EdgeMargin);
        var requestedTop = ballTop + ballDiameter - bubbleHeight;
        var minimumTop = workArea.Top + EdgeMargin;
        var maximumTop = Math.Max(
            minimumTop,
            workArea.Bottom - bubbleHeight - EdgeMargin);

        return new OverlayBounds(
            Math.Clamp(requestedLeft, minimumLeft, maximumLeft),
            Math.Clamp(requestedTop, minimumTop, maximumTop),
            bubbleWidth,
            bubbleHeight);
    }
}
