using InterviewAgent.Core.Corrections;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Core.Sessions;

namespace InterviewAgent.Core.Answers;

public sealed record AnswerRequest(
    string Question,
    IReadOnlyList<KnowledgeHit> Evidence,
    DomainCorrectionResult? Correction = null,
    IReadOnlyList<InterviewTurn>? Context = null);
