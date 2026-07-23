using InterviewAgent.Core.Knowledge;

namespace InterviewAgent.Core.Tests;

public sealed class KnowledgeChunkerTests
{
    [Fact]
    public void Chunk_PreservesHeadingPathAndQuestion()
    {
        var paragraphs = new[]
        {
            new StructuredParagraph("Java 基础", 1),
            new StructuredParagraph("并发编程", 2),
            new StructuredParagraph("什么是 volatile？", 0),
            new StructuredParagraph("volatile 保证可见性和有序性，但不保证复合操作的原子性。", 0),
            new StructuredParagraph("它通过内存屏障约束重排序。", 0),
        };

        var chunks = KnowledgeChunker.Chunk(paragraphs, targetCharacters: 120, maxCharacters: 240);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Java 基础 > 并发编程", chunk.HeadingPath);
        Assert.Equal("什么是 volatile？", chunk.Question);
        Assert.Contains("内存屏障", chunk.Content);
    }

    [Fact]
    public void Chunk_StartsNewChunkAtQuestionBoundary()
    {
        var paragraphs = new[]
        {
            new StructuredParagraph("数据库", 1),
            new StructuredParagraph("什么是索引？", 0),
            new StructuredParagraph("索引是帮助数据库快速定位记录的数据结构。", 0),
            new StructuredParagraph("什么是事务隔离级别？", 0),
            new StructuredParagraph("常见隔离级别包括读未提交、读已提交、可重复读和串行化。", 0),
        };

        var chunks = KnowledgeChunker.Chunk(paragraphs, targetCharacters: 500, maxCharacters: 900);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("什么是索引？", chunks[0].Question);
        Assert.Equal("什么是事务隔离级别？", chunks[1].Question);
    }

    [Fact]
    public void Chunk_SplitsOversizedContentWithoutExceedingMaximum()
    {
        var paragraphs = new[]
        {
            new StructuredParagraph("系统设计", 1),
            new StructuredParagraph("如何设计限流器？", 0),
            new StructuredParagraph(new string('答', 1_050), 0),
        };

        var chunks = KnowledgeChunker.Chunk(paragraphs, targetCharacters: 300, maxCharacters: 420);

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Content.Length, 1, 420));
    }
}

