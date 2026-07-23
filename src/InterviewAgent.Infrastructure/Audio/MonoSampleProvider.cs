using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace InterviewAgent.Infrastructure.Audio;

internal sealed class MonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _sourceBuffer;

    public MonoSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentOutOfRangeException.ThrowIfLessThan(source.WaveFormat.Channels, 1);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            source.WaveFormat.SampleRate,
            1);
        _sourceBuffer = new float[8_192 * source.WaveFormat.Channels];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _source.WaveFormat.Channels;
        var sourceCount = Math.Min(count * channels, _sourceBuffer.Length);
        var read = _source.Read(_sourceBuffer, 0, sourceCount);
        var frames = read / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += _sourceBuffer[(frame * channels) + channel];
            }

            buffer[offset + frame] = sum / channels;
        }

        return frames;
    }
}

