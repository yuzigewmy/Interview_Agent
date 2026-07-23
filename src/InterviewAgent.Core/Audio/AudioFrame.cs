namespace InterviewAgent.Core.Audio;

public sealed record AudioFrame(
    byte[] Pcm16,
    int SampleRate,
    int Channels,
    DateTimeOffset CapturedAt)
{
    public TimeSpan Duration =>
        TimeSpan.FromSeconds(Pcm16.Length / (double)(SampleRate * Channels * sizeof(short)));
}

