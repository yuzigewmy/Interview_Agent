using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using InterviewAgent.Infrastructure.Volcano;

namespace InterviewAgent.Core.Tests;

public sealed class VolcanoAsrProtocolTests
{
    [Fact]
    public void EncodeFullRequest_WritesSequenceAndCompressedJson()
    {
        const string json = """{"request":{"model_name":"bigmodel"}}""";

        var frame = VolcanoAsrProtocol.EncodeFullRequest(json);

        Assert.Equal(new byte[] { 0x11, 0x11, 0x11, 0x00 }, frame[..4]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(4, 4)));
        var payloadLength = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(8, 4));
        Assert.Equal(frame.Length - 12, payloadLength);
        Assert.Equal(json, Decompress(frame.AsSpan(12).ToArray()));
    }

    [Theory]
    [InlineData(2, 0x21)]
    [InlineData(-3, 0x23)]
    public void EncodeAudio_WritesExpectedFlagsAndSequence(int sequence, byte expectedTypeAndFlags)
    {
        var audio = new byte[] { 1, 2, 3, 4 };

        var frame = VolcanoAsrProtocol.EncodeAudio(sequence, audio);

        Assert.Equal((byte)0x11, frame[0]);
        Assert.Equal(expectedTypeAndFlags, frame[1]);
        Assert.Equal(sequence, BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(4, 4)));
        Assert.Equal(audio, DecompressBytes(frame.AsSpan(12).ToArray()));
    }

    [Fact]
    public void DecodeServerFrame_DecompressesJsonPayload()
    {
        const string json = """{"result":{"text":"什么是线程池？"}}""";
        var compressed = Compress(Encoding.UTF8.GetBytes(json));
        var frame = new byte[8 + compressed.Length];
        frame[0] = 0x11;
        frame[1] = 0x90;
        frame[2] = 0x11;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4, 4), compressed.Length);
        compressed.CopyTo(frame, 8);

        var decoded = VolcanoAsrProtocol.DecodeServerFrame(frame);

        Assert.Equal(0x09, decoded.MessageType);
        Assert.Equal(json, Encoding.UTF8.GetString(decoded.Payload));
    }

    private static string Decompress(byte[] bytes) =>
        Encoding.UTF8.GetString(DecompressBytes(bytes));

    private static byte[] DecompressBytes(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }
}

