using System.Text;
using InterviewAgent.Infrastructure.Volcano;

namespace InterviewAgent.Core.Tests;

public sealed class VolcanoTranscriptParserTests
{
    [Fact]
    public void Parse_ExtractsPartialText()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"result":{"text":"请介绍一下线程池","utterances":[{"text":"请介绍一下线程池","definite":false}]}}""");

        var result = VolcanoTranscriptParser.Parse(payload, frameIsLast: false);

        Assert.Equal("请介绍一下线程池", result.Text);
        Assert.False(result.IsFinal);
    }

    [Fact]
    public void Parse_MarksDefiniteUtterancesAsFinal()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"result":{"text":"什么是索引？","utterances":[{"text":"什么是索引？","definite":true}]}}""");

        var result = VolcanoTranscriptParser.Parse(payload, frameIsLast: false);

        Assert.True(result.IsFinal);
    }

    [Fact]
    public void Parse_ReturnsEmptyForReadyPayload()
    {
        var result = VolcanoTranscriptParser.Parse(
            Encoding.UTF8.GetBytes("""{"result":{}}"""),
            frameIsLast: false);

        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.IsFinal);
    }
}

