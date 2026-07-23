using InterviewAgent.Core.Knowledge;

namespace InterviewAgent.Core.Orchestration;

public enum AnswerUpdateKind
{
    Sources,
    CloudDelta,
    LocalFallback,
    Error,
    Completed,
}

public sealed record AnswerUpdate(
    Guid QuestionId,
    AnswerUpdateKind Kind,
    string Text,
    IReadOnlyList<KnowledgeHit> Evidence,
    TimeSpan Elapsed);

