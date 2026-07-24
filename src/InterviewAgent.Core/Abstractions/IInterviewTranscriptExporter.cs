using InterviewAgent.Core.Sessions;

namespace InterviewAgent.Core.Abstractions;

public interface IInterviewTranscriptExporter
{
    Task ExportAsync(
        string path,
        IReadOnlyList<InterviewTurn> turns,
        CancellationToken cancellationToken);
}
