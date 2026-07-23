using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using InterviewAgent.Core.Knowledge;

namespace InterviewAgent.Infrastructure.Documents;

public sealed partial class DocxParagraphReader
{
    public const long MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxParagraphs = 20_000;

    public Task<IReadOnlyList<StructuredParagraph>> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run<IReadOnlyList<StructuredParagraph>>(
            () => ReadCore(path, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<StructuredParagraph> ReadCore(
        string path,
        CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var mainPart = document.MainDocumentPart
            ?? throw new InvalidDataException("DOCX 缺少主文档。");
        var body = mainPart.Document.Body
            ?? throw new InvalidDataException("DOCX 主文档为空。");
        var styleLevels = ReadStyleLevels(mainPart);
        var result = new List<StructuredParagraph>();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= MaxParagraphs)
            {
                throw new InvalidDataException($"DOCX 段落数超过上限 {MaxParagraphs}。");
            }

            var text = paragraph.InnerText.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > 10_000)
            {
                throw new InvalidDataException("DOCX 中存在异常超长段落。");
            }

            result.Add(new StructuredParagraph(
                text,
                ResolveHeadingLevel(paragraph, styleLevels)));
        }

        return result;
    }

    private static Dictionary<string, int> ReadStyleLevels(MainDocumentPart mainPart)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles is null)
        {
            return result;
        }

        foreach (var style in styles.Elements<Style>())
        {
            var id = style.StyleId?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var level = style.StyleParagraphProperties?.OutlineLevel?.Val?.Value;
            if (level is not null && level.Value <= 8)
            {
                result[id] = level.Value + 1;
                continue;
            }

            var name = style.StyleName?.Val?.Value;
            var parsed = TryParseHeadingLevel(name) ?? TryParseHeadingLevel(id);
            if (parsed is not null)
            {
                result[id] = parsed.Value;
            }
        }

        return result;
    }

    private static int ResolveHeadingLevel(
        Paragraph paragraph,
        IReadOnlyDictionary<string, int> styleLevels)
    {
        var outline = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (outline is not null && outline.Value <= 8)
        {
            return outline.Value + 1;
        }

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId is null)
        {
            return 0;
        }

        if (styleLevels.TryGetValue(styleId, out var level))
        {
            return level;
        }

        return TryParseHeadingLevel(styleId) ?? 0;
    }

    private static int? TryParseHeadingLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = HeadingStyleRegex().Match(value);
        return match.Success && int.TryParse(match.Groups[1].Value, out var level)
            ? Math.Clamp(level, 1, 9)
            : null;
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("只支持 .docx 文件。", nameof(path));
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("找不到面经文件。", path);
        }

        if (file.Length is <= 0 or > MaxFileBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(path),
                $"DOCX 文件必须小于 {MaxFileBytes / 1024 / 1024} MB。");
        }

        Span<byte> signature = stackalloc byte[2];
        using var stream = file.OpenRead();
        if (stream.Read(signature) != signature.Length ||
            signature[0] != (byte)'P' ||
            signature[1] != (byte)'K')
        {
            throw new InvalidDataException("文件不是有效的 DOCX/ZIP 格式。");
        }
    }

    [GeneratedRegex(
        @"(?:heading|标题|outline)[ _-]?([1-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingStyleRegex();
}

