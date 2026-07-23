using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Audio;

namespace InterviewAgent.Infrastructure.Volcano;

public sealed class DemoStreamingTranscriber : IStreamingTranscriber
{
    private bool _started;

    public event EventHandler<TranscriptUpdate>? TranscriptUpdated;

    public event EventHandler<string>? TranscriptionFailed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_started)
        {
            throw new InvalidOperationException("演示转写已启动。");
        }

        _started = true;
        return Task.CompletedTask;
    }

    public ValueTask SendAudioAsync(
        AudioFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_started)
        {
            throw new InvalidOperationException("演示转写尚未启动。");
        }

        return ValueTask.CompletedTask;
    }

    public Task<string> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        return Task.FromResult(string.Empty);
    }

    public ValueTask DisposeAsync()
    {
        _started = false;
        GC.KeepAlive(TranscriptUpdated);
        GC.KeepAlive(TranscriptionFailed);
        return ValueTask.CompletedTask;
    }
}

