using InterviewAgent.Core.Knowledge;

namespace InterviewAgent.Core.Answers;

public sealed record AnswerRequest(
    string Question,
    IReadOnlyList<KnowledgeHit> Evidence);

