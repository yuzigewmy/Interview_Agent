namespace InterviewAgent.Core.Abstractions;

public interface IEmbeddingProvider
{
    string Name { get; }

    int Dimensions { get; }

    ValueTask<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

