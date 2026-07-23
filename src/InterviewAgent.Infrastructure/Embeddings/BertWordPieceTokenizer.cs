using System.Globalization;
using System.Text;

namespace InterviewAgent.Infrastructure.Embeddings;

public sealed class BertWordPieceTokenizer
{
    private const int MaximumVocabularySize = 100_000;
    private const int MaximumBasicTokenLength = 100;
    private readonly IReadOnlyDictionary<string, long> _tokenIds;
    private readonly int _maximumTokens;
    private readonly bool _lowerCase;
    private readonly long _unknownId;
    private readonly long _classificationId;
    private readonly long _separatorId;

    public BertWordPieceTokenizer(
        IEnumerable<string> vocabulary,
        int maxTokens = 512,
        bool lowerCase = false)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTokens, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxTokens, 512);

        var tokenIds = new Dictionary<string, long>(StringComparer.Ordinal);
        long vocabularyIndex = 0;
        foreach (var token in vocabulary)
        {
            if (vocabularyIndex >= MaximumVocabularySize)
            {
                throw new InvalidDataException("BERT 词表超过安全大小上限。");
            }

            if (!string.IsNullOrEmpty(token))
            {
                tokenIds.TryAdd(token, vocabularyIndex);
            }

            vocabularyIndex++;
        }

        _unknownId = RequireToken(tokenIds, "[UNK]");
        _classificationId = RequireToken(tokenIds, "[CLS]");
        _separatorId = RequireToken(tokenIds, "[SEP]");
        _ = RequireToken(tokenIds, "[PAD]");
        _tokenIds = tokenIds;
        _maximumTokens = maxTokens;
        _lowerCase = lowerCase;
    }

    public static BertWordPieceTokenizer FromFile(
        string vocabularyPath,
        int maxTokens = 512)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabularyPath);
        var fullPath = Path.GetFullPath(vocabularyPath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("未找到 BGE 词表文件。", fullPath);
        }

        if (file.Length is <= 0 or > 5 * 1024 * 1024)
        {
            throw new InvalidDataException("BGE 词表文件大小无效。");
        }

        return new BertWordPieceTokenizer(File.ReadLines(fullPath), maxTokens);
    }

    public BertTokenizedInput Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var inputIds = new List<long>(_maximumTokens)
        {
            _classificationId,
        };

        foreach (var basicToken in BasicTokenize(text))
        {
            foreach (var pieceId in WordPiece(basicToken))
            {
                if (inputIds.Count >= _maximumTokens - 1)
                {
                    goto Truncated;
                }

                inputIds.Add(pieceId);
            }
        }

    Truncated:
        inputIds.Add(_separatorId);
        var attentionMask = Enumerable.Repeat(1L, inputIds.Count).ToArray();
        return new BertTokenizedInput(
            inputIds.ToArray(),
            attentionMask,
            new long[inputIds.Count]);
    }

    private IEnumerable<string> BasicTokenize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        if (_lowerCase)
        {
            normalized = normalized.ToLowerInvariant();
        }

        var buffer = new StringBuilder();
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                foreach (var token in Flush(buffer))
                {
                    yield return token;
                }

                continue;
            }

            if (IsChinese(rune) || IsPunctuation(rune))
            {
                foreach (var token in Flush(buffer))
                {
                    yield return token;
                }

                yield return rune.ToString();
                continue;
            }

            buffer.Append(rune.ToString());
        }

        foreach (var token in Flush(buffer))
        {
            yield return token;
        }
    }

    private IEnumerable<long> WordPiece(string token)
    {
        if (token.Length > MaximumBasicTokenLength)
        {
            yield return _unknownId;
            yield break;
        }

        var pieceIds = new List<long>();
        var start = 0;
        while (start < token.Length)
        {
            var end = token.Length;
            long? foundId = null;
            var foundEnd = start;
            while (start < end)
            {
                var piece = token[start..end];
                if (start > 0)
                {
                    piece = $"##{piece}";
                }

                if (_tokenIds.TryGetValue(piece, out var id))
                {
                    foundId = id;
                    foundEnd = end;
                    break;
                }

                end--;
            }

            if (foundId is null)
            {
                yield return _unknownId;
                yield break;
            }

            pieceIds.Add(foundId.Value);
            start = foundEnd;
        }

        foreach (var id in pieceIds)
        {
            yield return id;
        }
    }

    private static IEnumerable<string> Flush(StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            yield break;
        }

        var value = buffer.ToString();
        buffer.Clear();
        yield return value;
    }

    private static long RequireToken(
        IReadOnlyDictionary<string, long> vocabulary,
        string token) =>
        vocabulary.TryGetValue(token, out var id)
            ? id
            : throw new InvalidDataException($"BERT 词表缺少必要标记 {token}。");

    private static bool IsChinese(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x3400 and <= 0x4DBF or
            >= 0x4E00 and <= 0x9FFF or
            >= 0x20000 and <= 0x2A6DF or
            >= 0x2A700 and <= 0x2B73F or
            >= 0x2B740 and <= 0x2B81F or
            >= 0x2B820 and <= 0x2CEAF or
            >= 0xF900 and <= 0xFAFF or
            >= 0x2F800 and <= 0x2FA1F;
    }

    private static bool IsPunctuation(Rune rune)
    {
        var value = rune.Value;
        if (value is >= 33 and <= 47 or
            >= 58 and <= 64 or
            >= 91 and <= 96 or
            >= 123 and <= 126)
        {
            return true;
        }

        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.DashPunctuation or
            UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation or
            UnicodeCategory.OtherPunctuation;
    }
}

public sealed record BertTokenizedInput(
    long[] InputIds,
    long[] AttentionMask,
    long[] TokenTypeIds);
