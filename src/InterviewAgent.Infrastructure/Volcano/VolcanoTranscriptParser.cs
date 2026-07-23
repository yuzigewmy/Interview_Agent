using System.Text.Json;
using InterviewAgent.Core.Audio;

namespace InterviewAgent.Infrastructure.Volcano;

public static class VolcanoTranscriptParser
{
    public static TranscriptUpdate Parse(ReadOnlySpan<byte> payload, bool frameIsLast)
    {
        if (payload.Length == 0)
        {
            return new TranscriptUpdate(string.Empty, frameIsLast);
        }

        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (!document.RootElement.TryGetProperty("result", out var result))
            {
                return new TranscriptUpdate(string.Empty, frameIsLast);
            }

            var text = result.TryGetProperty("text", out var textElement) &&
                       textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString() ?? string.Empty
                : string.Empty;
            var isFinal = frameIsLast;
            if (result.TryGetProperty("utterances", out var utterances) &&
                utterances.ValueKind == JsonValueKind.Array)
            {
                foreach (var utterance in utterances.EnumerateArray())
                {
                    if (utterance.TryGetProperty("definite", out var definite) &&
                        definite.ValueKind is JsonValueKind.True)
                    {
                        isFinal = true;
                    }
                }
            }

            return new TranscriptUpdate(text, isFinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("火山 ASR 返回了无效 JSON。", exception);
        }
    }
}
