using InterviewAgent.Infrastructure.Embeddings;

namespace InterviewAgent.Core.Tests;

public sealed class LocalHashEmbeddingProviderTests
{
    [Fact]
    public async Task EmbedAsync_IsDeterministicAndUnitNormalized()
    {
        var provider = new LocalHashEmbeddingProvider(256);

        var first = await provider.EmbedAsync("volatile 保证线程可见性", CancellationToken.None);
        var second = await provider.EmbedAsync("volatile 保证线程可见性", CancellationToken.None);

        Assert.Equal(256, first.Length);
        Assert.Equal(first, second);
        var norm = Math.Sqrt(first.Sum(value => value * value));
        Assert.InRange(norm, 0.9999, 1.0001);
    }

    [Fact]
    public async Task EmbedAsync_PlacesRelatedTextCloserThanUnrelatedText()
    {
        var provider = new LocalHashEmbeddingProvider(512);
        var question = await provider.EmbedAsync("volatile 有什么作用", CancellationToken.None);
        var related = await provider.EmbedAsync("volatile 保证可见性并禁止指令重排序", CancellationToken.None);
        var unrelated = await provider.EmbedAsync("红烧鱼需要先把鱼煎至两面金黄", CancellationToken.None);

        Assert.True(Cosine(question, related) > Cosine(question, unrelated));
    }

    private static double Cosine(float[] left, float[] right) =>
        left.Zip(right, (a, b) => a * b).Sum();
}

