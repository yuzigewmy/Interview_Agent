using InterviewAgent.Core.Overlay;

namespace InterviewAgent.Core.Tests;

public sealed class FloatingOverlayLayoutTests
{
    private static readonly OverlayBounds PrimaryWorkArea =
        new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(40, 56)]
    [InlineData(72, 72)]
    [InlineData(180, 128)]
    public void NormalizeDiameter_ClampsToSupportedRange(
        double requested,
        double expected)
    {
        Assert.Equal(expected, FloatingOverlayLayout.NormalizeDiameter(requested));
    }

    [Fact]
    public void ClampBallToWorkArea_SupportsNegativeMonitorCoordinates()
    {
        var workArea = new OverlayBounds(-1920, 0, 1920, 1040);

        var result = FloatingOverlayLayout.ClampBallToWorkArea(
            left: -2500,
            top: 1200,
            diameter: 72,
            workArea);

        Assert.Equal(-1912, result.Left);
        Assert.Equal(960, result.Top);
    }

    [Fact]
    public void ClampBallToWorkArea_KeepsVisiblePlacementUnchanged()
    {
        var result = FloatingOverlayLayout.ClampBallToWorkArea(
            left: 1600,
            top: 800,
            diameter: 72,
            PrimaryWorkArea);

        Assert.Equal(1600, result.Left);
        Assert.Equal(800, result.Top);
    }

    [Fact]
    public void ChooseBubbleSide_PrefersLeftWhenBothSidesFit()
    {
        var side = FloatingOverlayLayout.ChooseBubbleSide(
            ballLeft: 1600,
            ballDiameter: 72,
            bubbleWidth: 420,
            PrimaryWorkArea);

        Assert.Equal(OverlaySide.Left, side);
    }

    [Fact]
    public void ChooseBubbleSide_UsesRightNearLeftEdge()
    {
        var side = FloatingOverlayLayout.ChooseBubbleSide(
            ballLeft: 20,
            ballDiameter: 72,
            bubbleWidth: 420,
            PrimaryWorkArea);

        Assert.Equal(OverlaySide.Right, side);
    }

    [Fact]
    public void PositionBubble_ClampsVerticallyInsideWorkArea()
    {
        var result = FloatingOverlayLayout.PositionBubble(
            ballLeft: 1600,
            ballTop: 20,
            ballDiameter: 72,
            bubbleWidth: 420,
            bubbleHeight: 400,
            PrimaryWorkArea);

        Assert.Equal(8, result.Top);
        Assert.True(result.Left >= PrimaryWorkArea.Left);
        Assert.True(result.Right <= PrimaryWorkArea.Right);
    }
}
