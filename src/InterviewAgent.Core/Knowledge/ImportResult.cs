namespace InterviewAgent.Core.Knowledge;

public sealed record ImportResult(
    string SourceFileName,
    int ChunkCount,
    TimeSpan Duration,
    string EmbeddingProvider);

