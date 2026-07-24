using InterviewAgent.Core.Settings;

namespace InterviewAgent.Core.Tests;

public sealed class AppSettingsOverlayTests
{
    [Fact]
    public void Default_DisablesFloatingOverlayWithSafePlacement()
    {
        var settings = AppSettings.Default;

        Assert.False(settings.EnableFloatingOverlay);
        Assert.Equal(72, settings.FloatingOverlayDiameter);
        Assert.Null(settings.FloatingOverlayLeft);
        Assert.Null(settings.FloatingOverlayTop);
    }

    [Theory]
    [InlineData(55)]
    [InlineData(129)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_RejectsInvalidFloatingOverlayDiameter(double diameter)
    {
        var settings = AppSettings.Default with
        {
            FloatingOverlayDiameter = diameter,
        };

        Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
    }

    [Fact]
    public void Validate_RejectsNonFiniteFloatingOverlayCoordinates()
    {
        var settings = AppSettings.Default with
        {
            FloatingOverlayLeft = double.NegativeInfinity,
        };

        Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
    }

    [Fact]
    public void Validate_AllowsNegativeCoordinatesForSecondaryMonitor()
    {
        var settings = AppSettings.Default with
        {
            FloatingOverlayLeft = -1600,
            FloatingOverlayTop = 120,
            FloatingOverlayDiameter = 96,
        };

        settings.Validate();
    }
}
