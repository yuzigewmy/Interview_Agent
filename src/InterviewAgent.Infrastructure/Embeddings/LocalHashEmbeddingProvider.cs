using System.Text;
using System.Text.RegularExpressions;
using InterviewAgent.Core.Abstractions;

namespace InterviewAgent.Infrastructure.Embeddings;

public sealed partial class LocalHashEmbeddingProvider : IEmbeddingProvider
{
    public LocalHashEmbeddingProvider(int dimensions = 384)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 64);
        Dimensions = dimensions;
    }

    public string Name => "local-char-ngram";

    public int Dimensions { get; }

    public ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        var vector = new float[Dimensions];
        foreach (var token in Tokenize(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = Fnv1A(token);
            var index = (int)(hash % (uint)Dimensions);
            var sign = (hash & 0x80000000u) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        var squaredNorm = vector.Sum(value => value * value);
        if (squaredNorm > 0)
        {
            var inverseNorm = 1f / MathF.Sqrt(squaredNorm);
            for (var index = 0; index < vector.Length; index++)
            {
                vector[index] *= inverseNorm;
            }
        }

        return ValueTask.FromResult(vector);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        foreach (Match match in LatinWordRegex().Matches(normalized))
        {
            var word = match.Value;
            yield return $"w:{word}";
            if (word.Length >= 4)
            {
                for (var index = 0; index <= word.Length - 3; index++)
                {
                    yield return $"l:{word.AsSpan(index, 3).ToString()}";
                }
            }
        }

        foreach (Match match in ChineseSequenceRegex().Matches(normalized))
        {
            var sequence = match.Value;
            if (sequence.Length == 1)
            {
                yield return $"c:{sequence}";
                continue;
            }

            for (var index = 0; index < sequence.Length - 1; index++)
            {
                yield return $"c:{sequence.AsSpan(index, 2).ToString()}";
            }
        }
    }

    private static uint Fnv1A(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][a-z0-9_.+#]*", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWordRegex();

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}]+", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseSequenceRegex();
}

