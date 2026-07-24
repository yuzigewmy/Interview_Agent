using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InterviewAgent.Core.Corrections;
using InterviewAgent.Core.Text;

namespace InterviewAgent.Infrastructure.Corrections;

public sealed partial class DomainQuestionCorrector : IDomainQuestionCorrector
{
    private const int MaxConfigurationBytes = 1024 * 1024;
    private const int MaxEntries = 5_000;
    private const int MaxEntryCharacters = 80;
    private const int MaxReplacements = 8;
    private readonly PhraseMapping[] _mappings;
    private readonly IReadOnlySet<string> _protectedCanonicalTokens;
    private readonly IReadOnlyDictionary<int, DomainTerm[]> _termsByLength;

    internal DomainQuestionCorrector(
        IReadOnlyDictionary<string, string> corrections,
        IEnumerable<(string Term, int Weight)> domainTerms)
    {
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(domainTerms);
        if (corrections.Count > MaxEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(corrections),
                $"领域纠错映射不能超过 {MaxEntries} 条。");
        }

        _mappings = corrections
            .Select(pair => new PhraseMapping(
                NormalizeEntry(pair.Key, nameof(corrections)),
                NormalizeEntry(pair.Value, nameof(corrections))))
            .Where(mapping => !string.Equals(
                mapping.Alias,
                mapping.Canonical,
                StringComparison.Ordinal))
            .DistinctBy(mapping => mapping.Alias, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(mapping => mapping.Alias.Length)
            .ToArray();
        _protectedCanonicalTokens = _mappings
            .SelectMany(mapping => LatinTokenRegex()
                .Matches(mapping.Canonical)
                .Select(match => match.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _termsByLength = domainTerms
            .Select(term => new DomainTerm(
                NormalizeEntry(term.Term, nameof(domainTerms)),
                Math.Clamp(term.Weight, 1, 10)))
            .Where(term => LatinDomainTermRegex().IsMatch(term.Term))
            .DistinctBy(term => term.Term, StringComparer.OrdinalIgnoreCase)
            .GroupBy(term => term.Term.Length)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(term => term.Weight)
                    .ThenBy(term => term.Term, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    public static DomainQuestionCorrector LoadOrEmpty(
        string correctionsPath,
        string hotwordsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correctionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hotwordsPath);
        try
        {
            var corrections = LoadCorrections(correctionsPath);
            var terms = LoadDomainTerms(hotwordsPath);
            return new DomainQuestionCorrector(corrections, terms);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException or
                  InvalidDataException or
                  ArgumentException)
        {
            return new DomainQuestionCorrector(
                new Dictionary<string, string>(),
                Array.Empty<(string Term, int Weight)>());
        }
    }

    public DomainCorrectionResult Correct(string transcript)
    {
        var normalized = NormalizeTranscript(transcript);
        var dictionaryCandidate = ApplyDictionary(normalized);
        var dictionaryChanged = !string.Equals(
                dictionaryCandidate,
                normalized,
                StringComparison.Ordinal);
        var fuzzyCandidate = FindFuzzyCandidate(dictionaryCandidate);
        if (fuzzyCandidate is not null)
        {
            return new DomainCorrectionResult(
                normalized,
                fuzzyCandidate.Question,
                fuzzyCandidate.Confidence,
                dictionaryChanged
                    ? CorrectionSource.DictionaryAndFuzzy
                    : CorrectionSource.FuzzyDomainTerm);
        }

        if (dictionaryChanged)
        {
            return new DomainCorrectionResult(
                normalized,
                dictionaryCandidate,
                CorrectionConfidence.High,
                CorrectionSource.Dictionary);
        }

        return new DomainCorrectionResult(
            normalized,
            normalized,
            CorrectionConfidence.High,
            CorrectionSource.Original);
    }

    private string ApplyDictionary(string transcript)
    {
        var result = transcript;
        var replacementCount = 0;
        foreach (var mapping in _mappings)
        {
            result = ReplacePhrase(
                result,
                mapping,
                ref replacementCount);
            if (replacementCount >= MaxReplacements)
            {
                break;
            }
        }

        return result;
    }

    private FuzzyCandidate? FindFuzzyCandidate(string transcript)
    {
        FuzzyCandidate? best = null;
        foreach (Match match in LatinTokenRegex().Matches(transcript))
        {
            var token = match.Value;
            if (_protectedCanonicalTokens.Contains(token))
            {
                continue;
            }

            var candidates = GetTermsNearLength(token.Length)
                .Where(term => !string.Equals(
                    token,
                    term.Term,
                    StringComparison.OrdinalIgnoreCase))
                .Select(term => new
                {
                    DomainTerm = term,
                    Distance = LevenshteinDistance(token, term.Term, 2),
                })
                .Where(item =>
                    item.Distance is 1 or 2 &&
                    (item.Distance == 1 || item.DomainTerm.Weight >= 9))
                .OrderBy(item => item.Distance)
                .ThenByDescending(item => item.DomainTerm.Weight)
                .ThenBy(item => item.DomainTerm.Term, StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToArray();
            if (candidates.Length == 0 ||
                (candidates.Length > 1 &&
                 candidates[0].Distance == candidates[1].Distance &&
                 candidates[0].DomainTerm.Weight == candidates[1].DomainTerm.Weight))
            {
                continue;
            }

            var winner = candidates[0];
            var confidence = winner.Distance == 1 ||
                             winner.DomainTerm.Weight == 10
                ? CorrectionConfidence.Medium
                : CorrectionConfidence.Low;
            var question =
                transcript[..match.Index] +
                winner.DomainTerm.Term +
                transcript[(match.Index + match.Length)..];
            var current = new FuzzyCandidate(
                question,
                confidence,
                winner.Distance,
                winner.DomainTerm.Weight);
            if (best is null ||
                current.Distance < best.Distance ||
                (current.Distance == best.Distance &&
                 current.Weight > best.Weight))
            {
                best = current;
            }
        }

        return best;
    }

    private IEnumerable<DomainTerm> GetTermsNearLength(int length)
    {
        for (var candidateLength = Math.Max(3, length - 1);
             candidateLength <= length + 1;
             candidateLength++)
        {
            if (_termsByLength.TryGetValue(candidateLength, out var terms))
            {
                foreach (var term in terms)
                {
                    yield return term;
                }
            }
        }
    }

    private static string ReplacePhrase(
        string input,
        PhraseMapping mapping,
        ref int replacementCount)
    {
        var searchIndex = 0;
        while (searchIndex <= input.Length - mapping.Alias.Length &&
               replacementCount < MaxReplacements)
        {
            var matchIndex = input.IndexOf(
                mapping.Alias,
                searchIndex,
                StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                break;
            }

            if (!HasSafeAsciiBoundaries(input, mapping.Alias, matchIndex))
            {
                searchIndex = matchIndex + 1;
                continue;
            }

            var existing = input.Substring(matchIndex, mapping.Alias.Length);
            if (string.Equals(existing, mapping.Canonical, StringComparison.Ordinal))
            {
                searchIndex = matchIndex + existing.Length;
                continue;
            }

            input =
                input[..matchIndex] +
                mapping.Canonical +
                input[(matchIndex + mapping.Alias.Length)..];
            replacementCount++;
            searchIndex = matchIndex + mapping.Canonical.Length;
        }

        return input;
    }

    private static bool HasSafeAsciiBoundaries(
        string input,
        string alias,
        int matchIndex)
    {
        if (IsAsciiLetterOrDigit(alias[0]) &&
            matchIndex > 0 &&
            IsAsciiLetterOrDigit(input[matchIndex - 1]))
        {
            return false;
        }

        var endIndex = matchIndex + alias.Length;
        return !IsAsciiLetterOrDigit(alias[^1]) ||
               endIndex >= input.Length ||
               !IsAsciiLetterOrDigit(input[endIndex]);
    }

    private static int LevenshteinDistance(
        string left,
        string right,
        int maximum)
    {
        if (Math.Abs(left.Length - right.Length) > maximum)
        {
            return maximum + 1;
        }

        Span<int> previous = stackalloc int[right.Length + 1];
        Span<int> current = stackalloc int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost =
                    char.ToLowerInvariant(left[row - 1]) ==
                    char.ToLowerInvariant(right[column - 1])
                        ? 0
                        : 1;
                current[column] = Math.Min(
                    Math.Min(
                        current[column - 1] + 1,
                        previous[column] + 1),
                    previous[column - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximum)
            {
                return maximum + 1;
            }

            var temporary = previous;
            previous = current;
            current = temporary;
        }

        return previous[right.Length];
    }

    private static IReadOnlyDictionary<string, string> LoadCorrections(string path)
    {
        EnsureConfigurationFile(path);
        var corrections = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(path, Encoding.UTF8));
        return corrections ?? throw new InvalidDataException("领域纠错词典为空。");
    }

    private static IReadOnlyList<(string Term, int Weight)> LoadDomainTerms(string path)
    {
        EnsureConfigurationFile(path);
        var terms = new List<(string Term, int Weight)>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            var separator = line.LastIndexOf('|');
            if (separator <= 0 ||
                !int.TryParse(
                    line[(separator + 1)..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var weight) ||
                weight is < 1 or > 10)
            {
                continue;
            }

            terms.Add((line[..separator], weight));
            if (terms.Count > MaxEntries)
            {
                throw new InvalidDataException(
                    $"领域术语不能超过 {MaxEntries} 条。");
            }
        }

        return terms;
    }

    private static void EnsureConfigurationFile(string path)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Length is <= 0 or > MaxConfigurationBytes)
        {
            throw new InvalidDataException("领域纠错配置文件不存在或大小无效。");
        }
    }

    private static string NormalizeTranscript(string transcript)
    {
        _ = QuestionNormalizer.Normalize(transcript);
        var normalized = WhitespaceRegex().Replace(
            transcript.Trim().Normalize(NormalizationForm.FormKC),
            " ");
        if (normalized.Length > QuestionNormalizer.MaxQuestionCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transcript),
                $"问题不能超过 {QuestionNormalizer.MaxQuestionCharacters} 个字符。");
        }

        return normalized;
    }

    private static string NormalizeEntry(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("领域纠错词条不能为空。", parameterName);
        }

        var normalized = WhitespaceRegex().Replace(
            value.Trim().Normalize(NormalizationForm.FormKC),
            " ");
        if (normalized.Length is < 2 or > MaxEntryCharacters)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"领域纠错词条长度必须为 2 到 {MaxEntryCharacters}。");
        }

        return normalized;
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9';

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"[A-Za-z][A-Za-z0-9]{2,31}",
        RegexOptions.CultureInvariant)]
    private static partial Regex LatinTokenRegex();

    [GeneratedRegex(
        @"^[A-Za-z][A-Za-z0-9]{2,31}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LatinDomainTermRegex();

    private sealed record PhraseMapping(string Alias, string Canonical);

    private sealed record DomainTerm(string Term, int Weight);

    private sealed record FuzzyCandidate(
        string Question,
        CorrectionConfidence Confidence,
        int Distance,
        int Weight);
}
