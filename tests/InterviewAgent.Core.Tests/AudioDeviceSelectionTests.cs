using InterviewAgent.Core.Audio;

namespace InterviewAgent.Core.Tests;

public sealed class AudioDeviceSelectionTests
{
    [Fact]
    public void Select_PreservesPreviouslySelectedDevice()
    {
        var devices = new[]
        {
            new AudioDevice("output", "扬声器", AudioDeviceKind.SystemOutput),
            new AudioDevice("microphone", "麦克风", AudioDeviceKind.Microphone),
        };

        var selected = AudioDeviceSelection.Select(devices, "microphone");

        Assert.Equal("microphone", selected?.Id);
    }

    [Fact]
    public void Select_FallsBackToSystemOutput()
    {
        var devices = new[]
        {
            new AudioDevice("microphone", "麦克风", AudioDeviceKind.Microphone),
            new AudioDevice("output", "扬声器", AudioDeviceKind.SystemOutput),
        };

        var selected = AudioDeviceSelection.Select(devices, "removed-device");

        Assert.Equal("output", selected?.Id);
    }

    [Fact]
    public void Select_ReturnsNullForEmptyDeviceList()
    {
        var selected = AudioDeviceSelection.Select([], null);

        Assert.Null(selected);
    }
}
