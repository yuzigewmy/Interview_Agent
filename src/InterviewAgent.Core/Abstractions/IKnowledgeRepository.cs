using InterviewAgent.Core.Knowledge;

namespace InterviewAgent.Core.Abstractions;

public interface IKnowledgeRepository
{
    Task<ImportResult> ImportDocxAsync(string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        string question,
        int limit,
        CancellationToken cancellationToken);
}

