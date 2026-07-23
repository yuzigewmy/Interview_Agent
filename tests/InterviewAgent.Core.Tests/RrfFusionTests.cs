using InterviewAgent.Core.Retrieval;

namespace InterviewAgent.Core.Tests;

public sealed class RrfFusionTests
{
    [Fact]
    public void Fuse_PromotesDocumentFoundByBothRankers()
    {
        var lexical = new[]
        {
            new RankedDocument("a", 1),
            new RankedDocument("shared", 2),
            new RankedDocument("b", 3),
        };
        var semantic = new[]
        {
            new RankedDocument("c", 1),
            new RankedDocument("shared", 2),
            new RankedDocument("d", 3),
        };

        var result = RrfFusion.Fuse(new[] { lexical, semantic }, limit: 3, rankConstant: 60);

        Assert.Equal("shared", result[0].DocumentId);
        Assert.True(result[0].Score > result[1].Score);
    }

    [Fact]
    public void Fuse_IsDeterministicForTies()
    {
        var first = new[] { new RankedDocument("b", 1) };
        var second = new[] { new RankedDocument("a", 1) };

        var result = RrfFusion.Fuse(new[] { first, second }, limit: 2, rankConstant: 60);

        Assert.Equal(new[] { "a", "b" }, result.Select(item => item.DocumentId));
    }
}

