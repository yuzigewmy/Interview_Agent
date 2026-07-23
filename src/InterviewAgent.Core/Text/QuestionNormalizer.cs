using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace InterviewAgent.Core.Text;

public static partial class QuestionNormalizer
{
    public const int MaxQuestionCharacters = 2_000;

    public static string Normalize(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var trimmed = question.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("问题不能为空。", nameof(question));
        }

        if (trimmed.Length > MaxQuestionCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(question),
                $"问题不能超过 {MaxQuestionCharacters} 个字符。");
        }

        var compatibilityNormalized = trimmed.Normalize(NormalizationForm.FormKC);
        var collapsed = WhitespaceRegex().Replace(compatibilityNormalized, " ");
        return EndingPunctuationRegex()
            .Replace(collapsed, string.Empty)
            .Trim()
            .ToLower(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\?\!\。！？；;，,：:\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EndingPunctuationRegex();
}
