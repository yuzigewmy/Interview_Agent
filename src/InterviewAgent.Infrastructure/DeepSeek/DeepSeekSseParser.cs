using System.Text.Json;

namespace InterviewAgent.Infrastructure.DeepSeek;

public sealed record SseParseResult(string? Content, bool IsDone);

public static class DeepSeekSseParser
{
    public static SseParseResult Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) ||
            !line.StartsWith("data:", StringComparison.Ordinal))
        {
            return new SseParseResult(null, false);
        }

        var data = line["data:".Length..].Trim();
        if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
        {
            return new SseParseResult(null, true);
        }

        try
        {
            using var document = JsonDocument.Parse(data);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return new SseParseResult(null, false);
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta) ||
                !delta.TryGetProperty("content", out var content) ||
                content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new SseParseResult(null, false);
            }

            return new SseParseResult(content.GetString(), false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("DeepSeek 返回了无效的 SSE JSON。", exception);
        }
    }
}

