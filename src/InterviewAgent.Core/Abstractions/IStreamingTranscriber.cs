using InterviewAgent.Core.Audio;

namespace InterviewAgent.Core.Abstractions;

public interface IStreamingTranscriber : IAsyncDisposable
{
    event EventHandler<TranscriptUpdate>? TranscriptUpdated;

    event EventHandler<string>? TranscriptionFailed;

    Task StartAsync(CancellationToken cancellationToken);

    ValueTask SendAudioAsync(AudioFrame frame, CancellationToken cancellationToken);

    Task<string> StopAsync(CancellationToken cancellationToken);
}

