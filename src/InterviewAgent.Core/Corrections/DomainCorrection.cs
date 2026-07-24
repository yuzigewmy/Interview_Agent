namespace InterviewAgent.Core.Corrections;

public enum CorrectionConfidence
{
    Low,
    Medium,
    High,
}

public enum CorrectionSource
{
    Original,
    Dictionary,
    FuzzyDomainTerm,
    DictionaryAndFuzzy,
}

public sealed record DomainCorrectionResult(
    string OriginalTranscript,
    string CandidateQuestion,
    CorrectionConfidence Confidence,
    CorrectionSource Source)
{
    public bool IsChanged =>
        !string.Equals(
            OriginalTranscript,
            CandidateQuestion,
            StringComparison.Ordinal);
}

public interface IDomainQuestionCorrector
{
    DomainCorrectionResult Correct(string transcript);
}
