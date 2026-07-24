namespace InterviewAgent.Core.Audio;

public static class AudioDeviceSelection
{
    public static AudioDevice? Select(
        IReadOnlyList<AudioDevice> devices,
        string? previousDeviceId)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (!string.IsNullOrWhiteSpace(previousDeviceId))
        {
            var previous = devices.FirstOrDefault(
                device => string.Equals(
                    device.Id,
                    previousDeviceId,
                    StringComparison.Ordinal));
            if (previous is not null)
            {
                return previous;
            }
        }

        return devices.FirstOrDefault(
                   device => device.Kind == AudioDeviceKind.SystemOutput)
               ?? devices.FirstOrDefault();
    }
}
