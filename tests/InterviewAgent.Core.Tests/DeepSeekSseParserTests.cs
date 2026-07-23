using InterviewAgent.Infrastructure.DeepSeek;

namespace InterviewAgent.Core.Tests;

public sealed class DeepSeekSseParserTests
{
    [Fact]
    public void Parse_ExtractsContentDelta()
    {
        const string line =
            """data: {"choices":[{"delta":{"content":"我会先说明结论。"},"finish_reason":null}]}""";

        var result = DeepSeekSseParser.Parse(line);

        Assert.Equal("我会先说明结论。", result.Content);
        Assert.False(result.IsDone);
    }

    [Theory]
    [InlineData(": keep-alive")]
    [InlineData("")]
    [InlineData("event: ping")]
    public void Parse_IgnoresNonDataLines(string line)
    {
        var result = DeepSeekSseParser.Parse(line);

        Assert.Null(result.Content);
        Assert.False(result.IsDone);
    }

    [Fact]
    public void Parse_RecognizesDoneMarker()
    {
        var result = DeepSeekSseParser.Parse("data: [DONE]");

        Assert.True(result.IsDone);
        Assert.Null(result.Content);
    }

    [Fact]
    public void Parse_AllowsUsageChunkWithoutChoices()
    {
        var result = DeepSeekSseParser.Parse(
            """data: {"choices":[],"usage":{"total_tokens":42}}""");

        Assert.Null(result.Content);
        Assert.False(result.IsDone);
    }

    [Fact]
    public void Parse_RejectsMalformedDataEvent()
    {
        Assert.Throws<InvalidDataException>(
            () => DeepSeekSseParser.Parse("data: {not-json}"));
    }
}

