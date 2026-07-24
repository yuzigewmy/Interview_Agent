using System.Text;
using System.Text.RegularExpressions;

namespace InterviewAgent.Infrastructure.Documents;

public static partial class InterviewExportFileName
{
    private const int MaximumQuestionCharacters = 48;

    public static string Create(
        string firstQuestion,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(firstQuestion);
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var output = new StringBuilder(firstQuestion.Length);
        foreach (var character in firstQuestion.Trim())
        {
            output.Append(
                invalid.Contains(character) ||
                char.IsControl(character) ||
                character is '：' or '？'
                    ? '_'
                    : character);
        }

        var safeQuestion = WhitespaceRegex()
            .Replace(output.ToString(), " ")
            .Trim(' ', '.');
        if (safeQuestion.Length > MaximumQuestionCharacters)
        {
            safeQuestion = safeQuestion[..MaximumQuestionCharacters]
                .TrimEnd(' ', '.');
        }

        if (string.IsNullOrWhiteSpace(safeQuestion) ||
            safeQuestion.All(character =>
                character == '_' ||
                character == '.' ||
                char.IsWhiteSpace(character)))
        {
            safeQuestion = "面试记录";
        }

        return
            $"{safeQuestion}_{timestamp:yyyyMMdd_HHmmss}.docx";
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
