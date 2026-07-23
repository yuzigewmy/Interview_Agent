using System.Text;
using System.Text.RegularExpressions;

namespace InterviewAgent.Core.Knowledge;

public static partial class KnowledgeChunker
{
    public static IReadOnlyList<KnowledgeChunk> Chunk(
        IEnumerable<StructuredParagraph> paragraphs,
        int targetCharacters = 600,
        int maxCharacters = 900)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetCharacters, 50);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, targetCharacters);

        var result = new List<KnowledgeChunk>();
        var headings = new SortedDictionary<int, string>();
        var buffer = new StringBuilder();
        var question = string.Empty;
        var sequence = 0;

        void Flush()
        {
            var content = buffer.ToString().Trim();
            if (content.Length == 0)
            {
                return;
            }

            result.Add(new KnowledgeChunk(
                sequence++,
                string.Join(" > ", headings.Values),
                question,
                content));
            buffer.Clear();
        }

        foreach (var paragraph in paragraphs)
        {
            var text = NormalizeParagraph(paragraph.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (paragraph.HeadingLevel > 0)
            {
                Flush();
                foreach (var level in headings.Keys
                             .Where(level => level >= paragraph.HeadingLevel)
                             .ToArray())
                {
                    headings.Remove(level);
                }

                headings[paragraph.HeadingLevel] = text;
                question = string.Empty;
                continue;
            }

            if (LooksLikeQuestion(text))
            {
                Flush();
                question = text;
            }

            AppendBounded(text);
        }

        Flush();
        return result;

        void AppendBounded(string text)
        {
            var offset = 0;
            while (offset < text.Length)
            {
                var separatorLength = buffer.Length == 0 ? 0 : Environment.NewLine.Length;
                var capacity = maxCharacters - buffer.Length - separatorLength;
                if (capacity <= 0)
                {
                    Flush();
                    continue;
                }

                var take = Math.Min(capacity, text.Length - offset);
                if (separatorLength > 0)
                {
                    buffer.AppendLine();
                }

                buffer.Append(text, offset, take);
                offset += take;

                if (buffer.Length >= maxCharacters)
                {
                    Flush();
                }
            }
        }
    }

    private static string NormalizeParagraph(string text) =>
        WhitespaceRegex().Replace(text?.Trim() ?? string.Empty, " ");

    private static bool LooksLikeQuestion(string text) =>
        text.EndsWith('？') ||
        text.EndsWith('?') ||
        QuestionPrefixRegex().IsMatch(text);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"^(什么|如何|怎么|为什么|请问|请说|说说|介绍一下|谈谈|简述|描述|解释|.*有什么区别)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QuestionPrefixRegex();
}

