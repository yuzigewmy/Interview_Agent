namespace InterviewAgent.Core.Knowledge;

public sealed record KnowledgeChunk(
    int Sequence,
    string HeadingPath,
    string Question,
    string Content);

