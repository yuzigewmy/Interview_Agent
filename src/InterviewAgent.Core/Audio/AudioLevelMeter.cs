using System.Buffers.Binary;

namespace InterviewAgent.Core.Audio;

public static class AudioLevelMeter
{
    private const double NoiseFloorDb = -48;
    private const double FullLevelDb = -6;

    public static double MeasureNormalized(ReadOnlySpan<byte> pcm16)
    {
        var sampleCount = pcm16.Length / sizeof(short);
        if (sampleCount == 0)
        {
            return 0;
        }

        double squareSum = 0;
        for (var index = 0; index < sampleCount * sizeof(short); index += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16.Slice(index, sizeof(short)));
            squareSum += (double)sample * sample;
        }

        var rms = Math.Sqrt(squareSum / sampleCount) / short.MaxValue;
        if (rms <= 0)
        {
            return 0;
        }

        var decibels = 20 * Math.Log10(rms);
        return Math.Clamp(
            (decibels - NoiseFloorDb) / (FullLevelDb - NoiseFloorDb),
            0,
            1);
    }
}

public sealed class AudioLevelSmoother
{
    private const double AttackFactor = 0.65;
    private const double ReleaseFactor = 0.18;

    public double Current { get; private set; }

    public double Update(double measuredLevel)
    {
        var target = Math.Clamp(measuredLevel, 0, 1);
        var factor = target > Current
            ? AttackFactor
            : ReleaseFactor;
        Current += (target - Current) * factor;
        return Current;
    }

    public void Reset() => Current = 0;
}
