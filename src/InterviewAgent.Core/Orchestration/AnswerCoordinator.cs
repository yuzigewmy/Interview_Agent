using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;
using InterviewAgent.Core.Corrections;
using InterviewAgent.Core.Knowledge;

namespace InterviewAgent.Core.Orchestration;

public sealed class AnswerCoordinator
{
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly Func<IAnswerGenerator> _generatorFactory;
    private readonly TimeSpan _fallbackDelay;
    private readonly TimeSpan _totalTimeout;
    private readonly IDomainQuestionCorrector? _questionCorrector;
    private CancellationTokenSource? _activeRequest;

    public AnswerCoordinator(
        IKnowledgeRepository knowledgeRepository,
        Func<IAnswerGenerator> generatorFactory,
        TimeSpan? fallbackDelay = null,
        TimeSpan? totalTimeout = null,
        IDomainQuestionCorrector? questionCorrector = null)
    {
        _knowledgeRepository = knowledgeRepository ??
            throw new ArgumentNullException(nameof(knowledgeRepository));
        _generatorFactory = generatorFactory ??
            throw new ArgumentNullException(nameof(generatorFactory));
        _fallbackDelay = fallbackDelay ?? TimeSpan.FromMilliseconds(4_500);
        _totalTimeout = totalTimeout ?? TimeSpan.FromSeconds(30);
        _questionCorrector = questionCorrector;
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
        var worker = RunAsync(
            question,
            questionId,
            channel.Writer,
            requestCancellation.Token);

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
            var correction = _questionCorrector?.Correct(question) ??
                new DomainCorrectionResult(
                    question,
                    question,
                    CorrectionConfidence.High,
                    CorrectionSource.Original);
            var evidence = await _knowledgeRepository.SearchAsync(
                correction.CandidateQuestion,
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
            using var fallbackCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var cloudTask = GenerateCloudAsync();
            var fallbackTask = EmitFallbackAsync();
            await Task.WhenAll(cloudTask, fallbackTask);

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
                                       new AnswerRequest(question, evidence, correction),
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

                        await writer.WriteAsync(
                            new AnswerUpdate(
                                questionId,
                                AnswerUpdateKind.CloudDelta,
                                delta.Text,
                                evidence,
                                stopwatch.Elapsed),
                            cancellationToken);
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

                await writer.WriteAsync(
                    new AnswerUpdate(
                        questionId,
                        AnswerUpdateKind.LocalFallback,
                        BuildFallback(correction.CandidateQuestion, evidence),
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
