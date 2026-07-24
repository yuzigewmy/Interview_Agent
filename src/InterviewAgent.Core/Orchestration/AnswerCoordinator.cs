using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;
using InterviewAgent.Core.Corrections;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Core.Sessions;

namespace InterviewAgent.Core.Orchestration;

public sealed class AnswerCoordinator
{
    private static readonly TimeSpan DefaultFallbackDelay =
        TimeSpan.FromSeconds(4);
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly Func<IAnswerGenerator> _generatorFactory;
    private readonly TimeSpan _fallbackDelay;
    private readonly TimeSpan _totalTimeout;
    private readonly IDomainQuestionCorrector? _questionCorrector;
    private readonly IInterviewSession? _interviewSession;
    private CancellationTokenSource? _activeRequest;

    public AnswerCoordinator(
        IKnowledgeRepository knowledgeRepository,
        Func<IAnswerGenerator> generatorFactory,
        TimeSpan? fallbackDelay = null,
        TimeSpan? totalTimeout = null,
        IDomainQuestionCorrector? questionCorrector = null,
        IInterviewSession? interviewSession = null)
    {
        _knowledgeRepository = knowledgeRepository ??
            throw new ArgumentNullException(nameof(knowledgeRepository));
        _generatorFactory = generatorFactory ??
            throw new ArgumentNullException(nameof(generatorFactory));
        _fallbackDelay = fallbackDelay ?? DefaultFallbackDelay;
        _totalTimeout = totalTimeout ?? TimeSpan.FromSeconds(30);
        _questionCorrector = questionCorrector;
        _interviewSession = interviewSession;
        if (_fallbackDelay <= TimeSpan.Zero || _totalTimeout <= _fallbackDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fallbackDelay),
                "兜底时间必须大于零且小于总超时。");
        }
    }

    public async IAsyncEnumerable<AnswerUpdate> StreamAsync(
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var questionId = Guid.NewGuid();
        using var requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(_totalTimeout);
        var previous = Interlocked.Exchange(ref _activeRequest, requestCancellation);
        previous?.Cancel();

        var channel = Channel.CreateUnbounded<AnswerUpdate>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        var worker = Task.Run(
            () => RunAsync(
                question,
                questionId,
                channel.Writer,
                requestCancellation.Token),
            CancellationToken.None);

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            requestCancellation.Cancel();
            try
            {
                await worker;
            }
            catch (OperationCanceledException)
            {
            }

            Interlocked.CompareExchange(ref _activeRequest, null, requestCancellation);
        }
    }

    public void CancelCurrent() =>
        Volatile.Read(ref _activeRequest)?.Cancel();

    private async Task RunAsync(
        string question,
        Guid questionId,
        ChannelWriter<AnswerUpdate> writer,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var recentContext =
                _interviewSession?.GetRecentContext() ??
                Array.Empty<InterviewTurn>();
            var correction = _questionCorrector?.Correct(question) ??
                new DomainCorrectionResult(
                    question,
                    question,
                    CorrectionConfidence.High,
                    CorrectionSource.Original);
            var context = SelectRelevantContext(
                correction.CandidateQuestion,
                recentContext);
            var evidence = await _knowledgeRepository.SearchAsync(
                BuildRetrievalQuestion(
                    correction.CandidateQuestion,
                    context),
                4,
                cancellationToken);
            await writer.WriteAsync(
                new AnswerUpdate(
                    questionId,
                    AnswerUpdateKind.Sources,
                    string.Empty,
                    evidence,
                    stopwatch.Elapsed),
                cancellationToken);

            var firstCloudDelta = 0;
            var cloudAnswer = new StringBuilder();
            string? fallbackAnswer = null;
            using var fallbackCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fallbackTask = EmitFallbackAsync();
            var cloudTask = GenerateCloudAsync();
            await Task.WhenAll(cloudTask, fallbackTask);

            var finalAnswer = cloudAnswer.Length > 0
                ? cloudAnswer.ToString()
                : fallbackAnswer;
            if (!string.IsNullOrWhiteSpace(finalAnswer))
            {
                _interviewSession?.Add(
                    new InterviewTurn(
                        DateTimeOffset.Now,
                        correction.OriginalTranscript,
                        correction.CandidateQuestion,
                        finalAnswer));
            }

            await writer.WriteAsync(
                new AnswerUpdate(
                    questionId,
                    AnswerUpdateKind.Completed,
                    string.Empty,
                    evidence,
                    stopwatch.Elapsed),
                cancellationToken);

            async Task GenerateCloudAsync()
            {
                try
                {
                    var generator = _generatorFactory();
                    await foreach (var delta in generator.StreamAsync(
                                       new AnswerRequest(
                                           question,
                                           evidence,
                                           correction,
                                           context),
                                       cancellationToken))
                    {
                        if (string.IsNullOrEmpty(delta.Text))
                        {
                            continue;
                        }

                        if (Interlocked.Exchange(ref firstCloudDelta, 1) == 0)
                        {
                            fallbackCancellation.Cancel();
                        }

                        var remaining =
                            InMemoryInterviewSession.MaximumAnswerCharacters -
                            cloudAnswer.Length;
                        if (remaining <= 0)
                        {
                            break;
                        }

                        var boundedText = delta.Text.Length <= remaining
                            ? delta.Text
                            : delta.Text[..remaining];
                        cloudAnswer.Append(boundedText);
                        await writer.WriteAsync(
                            new AnswerUpdate(
                                questionId,
                                AnswerUpdateKind.CloudDelta,
                                boundedText,
                                evidence,
                                stopwatch.Elapsed),
                            cancellationToken);
                        if (cloudAnswer.Length >=
                            InMemoryInterviewSession.MaximumAnswerCharacters)
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    await writer.WriteAsync(
                        new AnswerUpdate(
                            questionId,
                            AnswerUpdateKind.Error,
                            "云端回答暂时不可用，已保留本地知识库结果。",
                            evidence,
                            stopwatch.Elapsed),
                        cancellationToken);
                }
            }

            async Task EmitFallbackAsync()
            {
                var remaining = _fallbackDelay - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(remaining, fallbackCancellation.Token);
                    }
                    catch (OperationCanceledException)
                        when (fallbackCancellation.IsCancellationRequested &&
                              !cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }

                if (Volatile.Read(ref firstCloudDelta) != 0)
                {
                    return;
                }

                fallbackAnswer = BuildFallback(
                    correction.CandidateQuestion,
                    evidence);
                await writer.WriteAsync(
                    new AnswerUpdate(
                        questionId,
                        AnswerUpdateKind.LocalFallback,
                        fallbackAnswer,
                        evidence,
                        stopwatch.Elapsed),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static string BuildRetrievalQuestion(
        string currentQuestion,
        IReadOnlyList<InterviewTurn> context)
    {
        if (context.Count == 0 ||
            !IsLikelyFollowUp(currentQuestion))
        {
            return currentQuestion;
        }

        return
            $"{context[^1].CorrectedQuestion}\n" +
            $"追问：{currentQuestion}";
    }

    private static IReadOnlyList<InterviewTurn> SelectRelevantContext(
        string currentQuestion,
        IReadOnlyList<InterviewTurn> context)
    {
        if (context.Count == 0 || !IsLikelyFollowUp(currentQuestion))
        {
            return Array.Empty<InterviewTurn>();
        }

        return context.TakeLast(2).ToArray();
    }

    private static bool IsLikelyFollowUp(string question)
    {
        if (question.Length > 60)
        {
            return false;
        }

        string[] markers =
        [
            "它",
            "这个",
            "那个",
            "这种",
            "该",
            "上述",
            "刚才",
            "上一个",
            "前面",
            "继续",
            "接着",
            "然后",
            "还有呢",
            "那它",
            "那这个",
        ];
        return markers.Any(
            marker => question.Contains(marker, StringComparison.Ordinal));
    }

    private static string BuildFallback(
        string question,
        IReadOnlyList<KnowledgeHit> evidence)
    {
        var hit = evidence.FirstOrDefault();
        if (hit is null)
        {
            return
                $"我会先从定义、工作机制和适用边界三个层面回答“{question.Trim()}”。" +
                "当前知识库没有直接命中，我会先说明通用原理，再结合具体场景补充取舍。";
        }

        var content = hit.Content.Trim();
        if (content.Length > 320)
        {
            content = content[..320] + "……";
        }

        return $"我先直接回答核心结论：{content}";
    }
}
