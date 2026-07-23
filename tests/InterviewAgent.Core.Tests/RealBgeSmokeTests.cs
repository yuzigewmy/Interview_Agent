using InterviewAgent.Infrastructure.Embeddings;

namespace InterviewAgent.Core.Tests;

public sealed class RealBgeSmokeTests
{
    [Fact]
    public async Task EmbedAsync_WhenLocalModelIsInstalled_ReturnsNormalizedVector()
    {
        var modelDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InterviewAgent",
            "models",
            "bge-small-zh-v1.5");
        var modelPath = Path.Combine(modelDirectory, "model.onnx");
        var vocabularyPath = Path.Combine(modelDirectory, "vocab.txt");
        if (!File.Exists(modelPath) || !File.Exists(vocabularyPath))
        {
            return;
        }

        using var provider = new OnnxBgeEmbeddingProvider(modelPath, vocabularyPath);

        var embedding = await provider.EmbedAsync(
            "volatile 如何保证线程可见性",
            CancellationToken.None);

        Assert.Equal(512, embedding.Length);
        var norm = Math.Sqrt(embedding.Sum(value => value * value));
        Assert.InRange(norm, 0.9999, 1.0001);
    }
}
