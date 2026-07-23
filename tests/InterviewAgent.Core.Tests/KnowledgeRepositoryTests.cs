using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using InterviewAgent.Infrastructure.Documents;
using InterviewAgent.Infrastructure.Embeddings;
using InterviewAgent.Infrastructure.Knowledge;

namespace InterviewAgent.Core.Tests;

public sealed class KnowledgeRepositoryTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(Path.GetTempPath(), $"interview-agent-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsync_RejectsNonDocxInput()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "knowledge.txt");
        await File.WriteAllTextAsync(path, "not a docx");
        var reader = new DocxParagraphReader();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => reader.ReadAsync(path, CancellationToken.None));

        Assert.Contains(".docx", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAndSearchAsync_ReturnsRelevantSource()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var documentPath = CreateKnowledgeDocument();
        var databasePath = Path.Combine(_temporaryDirectory, "knowledge.db");
        var repository = new SqliteKnowledgeRepository(
            databasePath,
            new DocxParagraphReader(),
            new LocalHashEmbeddingProvider(384));

        var imported = await repository.ImportDocxAsync(documentPath, CancellationToken.None);
        var hits = await repository.SearchAsync(
            "volatile 为什么能保证线程之间的可见性？",
            4,
            CancellationToken.None);

        Assert.True(imported.ChunkCount >= 2);
        Assert.NotEmpty(hits);
        Assert.Contains("volatile", hits[0].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Java 并发", hits[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_TreatsFtsOperatorsAsData()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var repository = new SqliteKnowledgeRepository(
            Path.Combine(_temporaryDirectory, "safe-query.db"),
            new DocxParagraphReader(),
            new LocalHashEmbeddingProvider(128));
        await repository.ImportDocxAsync(CreateKnowledgeDocument(), CancellationToken.None);

        var exception = await Record.ExceptionAsync(
            () => repository.SearchAsync("\" OR 1=1 -- NEAR(*)", 4, CancellationToken.None));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private string CreateKnowledgeDocument()
    {
        var path = Path.Combine(_temporaryDirectory, "面经-test.docx");
        using var document = WordprocessingDocument.Create(
            path,
            WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(
            new Body(
                Heading("Java 并发", "Heading1"),
                Text("什么是 volatile？"),
                Text("volatile 保证线程之间的可见性和有序性，但不保证复合操作的原子性。"),
                Text("它通过内存屏障约束指令重排序。"),
                Heading("数据库", "Heading1"),
                Text("什么是事务隔离级别？"),
                Text("常见隔离级别包括读未提交、读已提交、可重复读和串行化。")));
        mainPart.Document.Save();
        return path;
    }

    private static Paragraph Heading(string value, string styleId) =>
        new(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new DocumentFormat.OpenXml.Wordprocessing.Text(value)));

    private static Paragraph Text(string value) =>
        new(new Run(new DocumentFormat.OpenXml.Wordprocessing.Text(value)));
}
