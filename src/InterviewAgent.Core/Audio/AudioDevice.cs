namespace InterviewAgent.Core.Audio;

public enum AudioDeviceKind
{
    SystemOutput,
    Microphone,
}

public sealed record AudioDevice(
    string Id,
    string Name,
    AudioDeviceKind Kind)
{
    public string DisplayName =>
        Kind == AudioDeviceKind.SystemOutput
            ? $"系统音频 · {Name}"
            : $"麦克风 · {Name}";
}

