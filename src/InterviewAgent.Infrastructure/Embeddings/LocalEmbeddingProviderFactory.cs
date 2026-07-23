using InterviewAgent.Core.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace InterviewAgent.Infrastructure.Embeddings;

public static class LocalEmbeddingProviderFactory
{
    public static IEmbeddingProvider Create(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        var fullDirectory = Path.GetFullPath(modelDirectory);
        var modelPath = Path.Combine(fullDirectory, "model.onnx");
        var vocabularyPath = Path.Combine(fullDirectory, "vocab.txt");
        if (!File.Exists(modelPath) || !File.Exists(vocabularyPath))
        {
            return new LocalHashEmbeddingProvider(384);
        }

        try
        {
            return new OnnxBgeEmbeddingProvider(modelPath, vocabularyPath);
        }
        catch (Exception exception)
            when (exception is IOException or
                  InvalidDataException or
                  UnauthorizedAccessException or
                  OnnxRuntimeException or
                  DllNotFoundException or
                  TypeInitializationException)
        {
            return new LocalHashEmbeddingProvider(384);
        }
    }
}
