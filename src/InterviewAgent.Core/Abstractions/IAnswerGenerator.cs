using InterviewAgent.Core.Answers;

namespace InterviewAgent.Core.Abstractions;

public interface IAnswerGenerator
{
    IAsyncEnumerable<AnswerDelta> StreamAsync(
        AnswerRequest request,
        CancellationToken cancellationToken);
}

