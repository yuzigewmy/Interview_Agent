using System.Buffers.Binary;
using System.IO.Compression;

namespace InterviewAgent.Infrastructure.Volcano;

public sealed record VolcanoServerFrame(
    byte MessageType,
    byte Flags,
    int? Sequence,
    int? ErrorCode,
    byte[] Payload)
{
    public bool IsLast => (Flags & 0b0010) != 0;
}

public static class VolcanoAsrProtocol
{
    private const int MaxPayloadBytes = 2 * 1024 * 1024;

    public static byte[] EncodeFullRequest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return Encode(
            messageType: 0b0001,
            flags: 0b0001,
            sequence: 1,
            System.Text.Encoding.UTF8.GetBytes(json));
    }

    public static byte[] EncodeAudio(int sequence, ReadOnlySpan<byte> audio)
    {
        if (sequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        var flags = sequence < 0 ? (byte)0b0011 : (byte)0b0001;
        return Encode(0b0010, flags, sequence, audio);
    }

    public static VolcanoServerFrame DecodeServerFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 8)
        {
            throw new InvalidDataException("火山 ASR 响应帧过短。");
        }

        var headerSize = (frame[0] & 0x0F) * 4;
        if (headerSize < 4 || headerSize > frame.Length)
        {
            throw new InvalidDataException("火山 ASR 响应头长度无效。");
        }

        var messageType = (byte)(frame[1] >> 4);
        var flags = (byte)(frame[1] & 0x0F);
        var compression = (byte)(frame[2] & 0x0F);
        var offset = headerSize;
        int? sequence = null;
        if ((flags & 0b0001) != 0)
        {
            EnsureRemaining(frame, offset, 4);
            sequence = BinaryPrimitives.ReadInt32BigEndian(frame.Slice(offset, 4));
            offset += 4;
        }

        int? errorCode = null;
        if (messageType == 0b1111)
        {
            EnsureRemaining(frame, offset, 8);
            errorCode = BinaryPrimitives.ReadInt32BigEndian(frame.Slice(offset, 4));
            offset += 4;
        }

        EnsureRemaining(frame, offset, 4);
        var payloadSize = BinaryPrimitives.ReadInt32BigEndian(frame.Slice(offset, 4));
        offset += 4;
        if (payloadSize < 0 || payloadSize > MaxPayloadBytes)
        {
            throw new InvalidDataException("火山 ASR 响应负载长度无效。");
        }

        EnsureRemaining(frame, offset, payloadSize);
        var payload = frame.Slice(offset, payloadSize).ToArray();
        if (compression == 0b0001)
        {
            payload = Decompress(payload);
        }
        else if (compression != 0)
        {
            throw new InvalidDataException("火山 ASR 使用了不支持的压缩格式。");
        }

        return new VolcanoServerFrame(messageType, flags, sequence, errorCode, payload);
    }

    private static byte[] Encode(
        byte messageType,
        byte flags,
        int sequence,
        ReadOnlySpan<byte> payload)
    {
        var compressed = Compress(payload);
        var frame = new byte[12 + compressed.Length];
        frame[0] = 0x11;
        frame[1] = (byte)((messageType << 4) | flags);
        frame[2] = 0x11;
        frame[3] = 0x00;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4, 4), sequence);
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(8, 4), compressed.Length);
        compressed.CopyTo(frame, 12);
        return frame;
    }

    private static byte[] Compress(ReadOnlySpan<byte> payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(
                   output,
                   CompressionLevel.Fastest,
                   leaveOpen: true))
        {
            gzip.Write(payload);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        if (output.Length > MaxPayloadBytes)
        {
            throw new InvalidDataException("火山 ASR 解压后负载超过上限。");
        }

        return output.ToArray();
    }

    private static void EnsureRemaining(
        ReadOnlySpan<byte> frame,
        int offset,
        int required)
    {
        if (required < 0 || offset < 0 || offset > frame.Length - required)
        {
            throw new InvalidDataException("火山 ASR 响应帧被截断。");
        }
    }
}

