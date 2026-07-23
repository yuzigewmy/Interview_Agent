namespace InterviewAgent.Core.Knowledge;

public sealed record KnowledgeHit(
    string Id,
    string Title,
    string Content,
    string SourceFileName,
    double Score);

