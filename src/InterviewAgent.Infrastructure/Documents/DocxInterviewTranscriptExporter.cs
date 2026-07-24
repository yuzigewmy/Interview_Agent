using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Sessions;

namespace InterviewAgent.Infrastructure.Documents;

public sealed class DocxInterviewTranscriptExporter
    : IInterviewTranscriptExporter
{
    private const string LatinFont = "Calibri";
    private const string EastAsiaFont = "Microsoft YaHei";
    private const string HeadingBlue = "2E74B5";
    private const string DarkBlue = "0B2545";
    private const string MutedGray = "667085";

    public Task ExportAsync(
        string path,
        IReadOnlyList<InterviewTurn> turns,
        CancellationToken cancellationToken)
    {
        Validate(path, turns);
        var fullPath = Path.GetFullPath(path);
        return Task.Run(
            () => ExportCore(fullPath, turns, cancellationToken),
            cancellationToken);
    }

    private static void ExportCore(
        string path,
        IReadOnlyList<InterviewTurn> turns,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var document = WordprocessingDocument.Create(
                       temporaryPath,
                       WordprocessingDocumentType.Document,
                       autoSave: true))
            {
                document.PackageProperties.Title = "面试问题与回答记录";
                document.PackageProperties.Subject = "Interview Agent 会话导出";
                document.PackageProperties.Creator = "Interview Agent";
                document.PackageProperties.Created = DateTime.UtcNow;

                var mainPart = document.AddMainDocumentPart();
                AddStyles(mainPart);
                var body = new Body();
                mainPart.Document = new Document(body);
                AddTitleBlock(body, turns);

                for (var index = 0; index < turns.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddTurn(body, turns[index], index + 1);
                }

                AddSection(mainPart, body);
                mainPart.Document.Save();
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void AddTitleBlock(
        Body body,
        IReadOnlyList<InterviewTurn> turns)
    {
        body.Append(
            CreateParagraph(
                "面试问题与回答记录",
                "InterviewTitle"));
        body.Append(
            CreateParagraph(
                $"导出时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}  ·  " +
                $"共 {turns.Count} 道问题",
                "InterviewSubtitle"));
    }

    private static void AddTurn(
        Body body,
        InterviewTurn turn,
        int number)
    {
        body.Append(
            CreateParagraph(
                $"问题 {number}  ·  {turn.RecordedAt:HH:mm:ss}",
                "Heading1"));
        if (!string.Equals(
                turn.OriginalQuestion,
                turn.CorrectedQuestion,
                StringComparison.Ordinal))
        {
            body.Append(
                CreateParagraph(
                    $"原始转写：{turn.OriginalQuestion}",
                    "InterviewCorrectionMetadata"));
            body.Append(
                CreateParagraph(
                    $"纠正后问题：{turn.CorrectedQuestion}",
                    "InterviewQuestion"));
        }
        else
        {
            body.Append(
                CreateParagraph(
                    turn.CorrectedQuestion,
                    "InterviewQuestion"));
        }

        body.Append(CreateParagraph("回答", "Heading2"));
        foreach (var paragraph in SplitParagraphs(turn.Answer))
        {
            body.Append(CreateParagraph(paragraph, "Normal"));
        }
    }

    private static IEnumerable<string> SplitParagraphs(string value)
    {
        var paragraphs = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .ToArray();
        return paragraphs.Length == 0 ? [value.Trim()] : paragraphs;
    }

    private static Paragraph CreateParagraph(
        string text,
        string styleId)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId { Val = styleId });
        var run = new Run(
            new Text(text)
            {
                Space = SpaceProcessingModeValues.Preserve,
            });
        return new Paragraph(properties, run);
    }

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            CreateParagraphStyle(
                "Normal",
                "Normal",
                fontSizeHalfPoints: "22",
                color: "182230",
                before: 0,
                after: 120,
                line: 300,
                isDefault: true),
            CreateParagraphStyle(
                "InterviewTitle",
                "Interview Title",
                fontSizeHalfPoints: "48",
                color: DarkBlue,
                before: 0,
                after: 160,
                line: 300,
                bold: true),
            CreateParagraphStyle(
                "InterviewSubtitle",
                "Interview Subtitle",
                fontSizeHalfPoints: "21",
                color: MutedGray,
                before: 0,
                after: 360,
                line: 280),
            CreateParagraphStyle(
                "Heading1",
                "Heading 1",
                fontSizeHalfPoints: "32",
                color: HeadingBlue,
                before: 360,
                after: 200,
                line: 300,
                bold: true,
                keepWithNext: true),
            CreateParagraphStyle(
                "Heading2",
                "Heading 2",
                fontSizeHalfPoints: "26",
                color: HeadingBlue,
                before: 280,
                after: 140,
                line: 300,
                bold: true,
                keepWithNext: true),
            CreateParagraphStyle(
                "InterviewQuestion",
                "Interview Question",
                fontSizeHalfPoints: "22",
                color: DarkBlue,
                before: 0,
                after: 100,
                line: 300,
                bold: true),
            CreateParagraphStyle(
                "InterviewMetadata",
                "Interview Metadata",
                fontSizeHalfPoints: "19",
                color: MutedGray,
                before: 0,
                after: 80,
                line: 280),
            CreateParagraphStyle(
                "InterviewCorrectionMetadata",
                "Interview Correction Metadata",
                fontSizeHalfPoints: "19",
                color: MutedGray,
                before: 0,
                after: 80,
                line: 280,
                keepWithNext: true));
        stylesPart.Styles.Save();
    }

    private static Style CreateParagraphStyle(
        string id,
        string name,
        string fontSizeHalfPoints,
        string color,
        int before,
        int after,
        int line,
        bool bold = false,
        bool keepWithNext = false,
        bool isDefault = false)
    {
        var paragraphProperties = new StyleParagraphProperties();
        if (keepWithNext)
        {
            paragraphProperties.Append(new KeepNext());
            paragraphProperties.Append(new KeepLines());
        }

        paragraphProperties.Append(
            new SpacingBetweenLines
            {
                Before = before.ToString(),
                After = after.ToString(),
                Line = line.ToString(),
                LineRule = LineSpacingRuleValues.Auto,
            });

        var runProperties = new StyleRunProperties(
            new RunFonts
            {
                Ascii = LatinFont,
                HighAnsi = LatinFont,
                EastAsia = EastAsiaFont,
                ComplexScript = LatinFont,
            });
        if (bold)
        {
            runProperties.Append(new Bold());
            runProperties.Append(new BoldComplexScript());
        }

        runProperties.Append(new Color { Val = color });
        runProperties.Append(new FontSize { Val = fontSizeHalfPoints });
        runProperties.Append(
            new FontSizeComplexScript { Val = fontSizeHalfPoints });

        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = id == "Normal" ? null : "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            paragraphProperties,
            runProperties)
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
            Default = isDefault,
            CustomStyle = id != "Normal",
        };
    }

    private static void AddSection(
        MainDocumentPart mainPart,
        Body body)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(
            new Paragraph(
                new ParagraphProperties(
                    new Justification
                    {
                        Val = JustificationValues.Center,
                    }),
                new Run(
                    new RunProperties(
                        new RunFonts
                        {
                            Ascii = LatinFont,
                            HighAnsi = LatinFont,
                            EastAsia = EastAsiaFont,
                        },
                        new Color { Val = MutedGray },
                        new FontSize { Val = "18" }),
                    new Text("Interview Agent · 面试记录 · 第 ")),
                new Run(
                    new FieldChar
                    {
                        FieldCharType = FieldCharValues.Begin,
                    }),
                new Run(
                    new FieldCode(" PAGE ")
                    {
                        Space = SpaceProcessingModeValues.Preserve,
                    }),
                new Run(
                    new FieldChar
                    {
                        FieldCharType = FieldCharValues.Separate,
                    }),
                new Run(new Text("1")),
                new Run(
                    new FieldChar
                    {
                        FieldCharType = FieldCharValues.End,
                    }),
                new Run(new Text(" 页"))));
        footerPart.Footer.Save();

        body.Append(
            new SectionProperties(
                new FooterReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = mainPart.GetIdOfPart(footerPart),
                },
                new PageSize
                {
                    Width = 12_240,
                    Height = 15_840,
                },
                new PageMargin
                {
                    Top = 1_440,
                    Right = 1_440U,
                    Bottom = 1_440,
                    Left = 1_440U,
                    Header = 708U,
                    Footer = 708U,
                    Gutter = 0U,
                }));
    }

    private static void Validate(
        string path,
        IReadOnlyList<InterviewTurn> turns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(turns);
        if (!string.Equals(
                Path.GetExtension(path),
                ".docx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("导出文件必须使用 .docx 扩展名。", nameof(path));
        }

        if (turns.Count == 0)
        {
            throw new InvalidOperationException("当前没有可导出的面试问题和答案。");
        }

        if (turns.Count > InMemoryInterviewSession.MaximumRecords)
        {
            throw new ArgumentOutOfRangeException(
                nameof(turns),
                "面试记录数量超过导出上限。");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("导出目录不存在。");
        }
    }
}
