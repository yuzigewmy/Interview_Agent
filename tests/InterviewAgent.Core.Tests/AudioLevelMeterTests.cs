using InterviewAgent.Core.Audio;

namespace InterviewAgent.Core.Tests;

public sealed class AudioLevelMeterTests
{
    [Fact]
    public void MeasureNormalized_WithSilence_ReturnsZero()
    {
        var pcm16 = new byte[3200];

        var level = AudioLevelMeter.MeasureNormalized(pcm16);

        Assert.Equal(0, level);
    }

    [Fact]
    public void MeasureNormalized_WithLouderSignal_ReturnsHigherLevel()
    {
        var quiet = CreateConstantPcm16(500, 1600);
        var loud = CreateConstantPcm16(12_000, 1600);

        var quietLevel = AudioLevelMeter.MeasureNormalized(quiet);
        var loudLevel = AudioLevelMeter.MeasureNormalized(loud);

        Assert.InRange(quietLevel, 0, 1);
        Assert.InRange(loudLevel, 0, 1);
        Assert.True(loudLevel > quietLevel);
    }

    [Fact]
    public void MeasureNormalized_WithMaximumSignal_IsClamped()
    {
        var pcm16 = CreateConstantPcm16(short.MaxValue, 1600);

        var level = AudioLevelMeter.MeasureNormalized(pcm16);

        Assert.Equal(1, level);
    }

    [Fact]
    public void MeasureNormalized_WithIncompleteSample_IgnoresTrailingByte()
    {
        var pcm16 = new byte[] { 0x10, 0x27, 0x7F };

        var level = AudioLevelMeter.MeasureNormalized(pcm16);

        Assert.InRange(level, 0, 1);
    }

    [Fact]
    public void Smoother_RisesFasterThanItFalls()
    {
        var smoother = new AudioLevelSmoother();

        var risen = smoother.Update(1);
        var fallen = smoother.Update(0);

        Assert.Equal(0.65, risen, precision: 5);
        Assert.Equal(0.533, fallen, precision: 5);
    }

    [Fact]
    public void Smoother_ResetClearsCurrentLevel()
    {
        var smoother = new AudioLevelSmoother();
        smoother.Update(1);

        smoother.Reset();

        Assert.Equal(0, smoother.Current);
    }

    private static byte[] CreateConstantPcm16(short sample, int sampleCount)
    {
        var bytes = new byte[sampleCount * sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            bytes[index * 2] = (byte)(sample & 0xFF);
            bytes[(index * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return bytes;
    }
}
