namespace InterviewAgent.Core.Retrieval;

public sealed record RankedDocument(string DocumentId, int Rank);

public sealed record FusedDocument(string DocumentId, double Score);

public static class RrfFusion
{
    public static IReadOnlyList<FusedDocument> Fuse(
        IEnumerable<IEnumerable<RankedDocument>> rankings,
        int limit,
        int rankConstant = 60)
    {
        ArgumentNullException.ThrowIfNull(rankings);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rankConstant, 1);

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var ranking in rankings)
        {
            foreach (var document in ranking)
            {
                if (string.IsNullOrWhiteSpace(document.DocumentId) || document.Rank < 1)
                {
                    continue;
                }

                scores.TryGetValue(document.DocumentId, out var current);
                scores[document.DocumentId] =
                    current + (1d / (rankConstant + document.Rank));
            }
        }

        return scores
            .Select(pair => new FusedDocument(pair.Key, pair.Value))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DocumentId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }
}

