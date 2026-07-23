using InterviewAgent.Core.Audio;

namespace InterviewAgent.Core.Abstractions;

public interface IAudioCaptureService : IDisposable
{
    event EventHandler<AudioFrame>? FrameCaptured;

    event EventHandler<string>? CaptureFailed;

    bool IsCapturing { get; }

    Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(
        CancellationToken cancellationToken);

    Task StartAsync(AudioDevice device, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

