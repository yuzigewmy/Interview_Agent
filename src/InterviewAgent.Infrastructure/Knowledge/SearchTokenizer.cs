using System.Text;
using System.Text.RegularExpressions;

namespace InterviewAgent.Infrastructure.Knowledge;

internal static partial class SearchTokenizer
{
    public static string ForIndex(string text) =>
        string.Join(' ', GetTerms(text));

    public static string ForMatch(string text) =>
        string.Join(" OR ", GetTerms(text).Select(term => $"\"{term}\""));

    private static IReadOnlyList<string> GetTerms(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var terms = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in LatinWordRegex().Matches(normalized))
        {
            terms.Add(match.Value);
        }

        foreach (Match match in ChineseSequenceRegex().Matches(normalized))
        {
            var sequence = match.Value;
            if (sequence.Length == 1)
            {
                terms.Add(sequence);
                continue;
            }

            for (var index = 0; index < sequence.Length - 1; index++)
            {
                terms.Add(sequence.Substring(index, 2));
            }
        }

        return terms.Take(2_000).ToArray();
    }

    [GeneratedRegex(@"[a-z0-9][a-z0-9_.+#]*", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWordRegex();

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}]+", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseSequenceRegex();
}

