using InterviewAgent.Core.Text;

namespace InterviewAgent.Core.Tests;

public sealed class QuestionNormalizerTests
{
    [Theory]
    [InlineData("  请问  JVM   的内存模型是什么？ ", "请问 jvm 的内存模型是什么")]
    [InlineData("HashMap 和 ConcurrentHashMap 有什么区别??", "hashmap 和 concurrenthashmap 有什么区别")]
    [InlineData("TCP／IP 三次握手。", "tcp/ip 三次握手")]
    public void Normalize_RemovesNoiseButPreservesMeaning(string input, string expected)
    {
        Assert.Equal(expected, QuestionNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_RejectsBlankQuestion()
    {
        Assert.Throws<ArgumentException>(() => QuestionNormalizer.Normalize(" \r\n "));
    }

    [Fact]
    public void Normalize_RejectsOversizedQuestion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuestionNormalizer.Normalize(new string('问', 2_001)));
    }
}

