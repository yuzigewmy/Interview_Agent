using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using InterviewAgent.Core.Sessions;
using InterviewAgent.Infrastructure.Documents;

namespace InterviewAgent.Core.Tests;

public sealed class DocxInterviewTranscriptExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"interview-record-{Guid.NewGuid():N}.docx");

    [Fact]
    public async Task ExportAsync_CreatesStructuredWordDocument()
    {
        var turns = new[]
        {
            new InterviewTurn(
                DateTimeOffset.Parse("2026-07-24T10:01:00+08:00"),
                "circle注入是什么？",
                "SQL注入是什么？",
                "SQL注入是把恶意 SQL 片段带入数据库查询。"),
            new InterviewTurn(
                DateTimeOffset.Parse("2026-07-24T10:02:00+08:00"),
                "那它怎么防御？",
                "那它怎么防御？",
                "我会优先使用参数化查询。\n同时限制数据库权限。"),
        };
        var exporter = new DocxInterviewTranscriptExporter();

        await exporter.ExportAsync(_path, turns, CancellationToken.None);

        using var document = WordprocessingDocument.Open(_path, isEditable: false);
        var validationErrors = new OpenXmlValidator()
            .Validate(document)
            .ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(
                Environment.NewLine,
                validationErrors.Select(error =>
                    $"{error.Description} | {error.Node?.OuterXml}")));
        var mainPart = Assert.IsType<MainDocumentPart>(document.MainDocumentPart);
        var body = Assert.IsType<Body>(mainPart.Document.Body);
        Assert.Contains("面试问题与回答记录", body.InnerText, StringComparison.Ordinal);
        Assert.Contains("问题 1", body.InnerText, StringComparison.Ordinal);
        Assert.Contains("原始转写：circle注入是什么？", body.InnerText, StringComparison.Ordinal);
        Assert.Contains("纠正后问题：SQL注入是什么？", body.InnerText, StringComparison.Ordinal);
        Assert.Contains("我会优先使用参数化查询", body.InnerText, StringComparison.Ordinal);

        var styles = mainPart.StyleDefinitionsPart?.Styles;
        Assert.NotNull(styles);
        Assert.Contains(
            styles!.Elements<Style>(),
            style => style.StyleId?.Value == "Heading1");
        var section = Assert.Single(body.Elements<SectionProperties>());
        Assert.Equal(12240U, section.GetFirstChild<PageSize>()?.Width?.Value);
        Assert.Equal(15840U, section.GetFirstChild<PageSize>()?.Height?.Value);
        Assert.Equal(1440, section.GetFirstChild<PageMargin>()?.Top?.Value);
        Assert.NotNull(mainPart.FooterParts.SingleOrDefault());
        Assert.Contains(
            "PAGE",
            mainPart.FooterParts.Single().Footer.InnerText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_RejectsEmptyInterviewSession()
    {
        var exporter = new DocxInterviewTranscriptExporter();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => exporter.ExportAsync(
                _path,
                Array.Empty<InterviewTurn>(),
                CancellationToken.None));

        Assert.Contains("没有可导出", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
