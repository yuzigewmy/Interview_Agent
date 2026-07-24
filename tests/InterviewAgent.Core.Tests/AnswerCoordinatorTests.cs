using System.Diagnostics;
using System.Runtime.CompilerServices;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;
using InterviewAgent.Core.Corrections;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Core.Orchestration;
using InterviewAgent.Core.Sessions;

namespace InterviewAgent.Core.Tests;

public sealed class AnswerCoordinatorTests
{
    [Fact]
    public async Task StreamAsync_EmitsLocalFallbackWhenCloudHasNoFirstToken()
    {
        var coordinator = new AnswerCoordinator(
            new FakeRepository(),
            () => new HangingGenerator(),
            TimeSpan.FromMilliseconds(45),
            TimeSpan.FromSeconds(2));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();
        AnswerUpdate? fallback = null;

        await foreach (var update in coordinator.StreamAsync(
                           "什么是 volatile？",
                           timeout.Token))
        {
            if (update.Kind == AnswerUpdateKind.LocalFallback)
            {
                fallback = update;
                break;
            }
        }

        Assert.NotNull(fallback);
        Assert.Contains("可见性", fallback.Text, StringComparison.Ordinal);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 20, 500);
    }

    [Fact]
    public async Task StreamAsync_DoesNotEmitFallbackWhenCloudRespondsFirst()
    {
        var coordinator = new AnswerCoordinator(
            new FakeRepository(),
            () => new ImmediateGenerator(),
            TimeSpan.FromMilliseconds(150),
            TimeSpan.FromSeconds(2));
        var updates = new List<AnswerUpdate>();

        await foreach (var update in coordinator.StreamAsync(
                           "什么是 volatile？",
                           CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.Contains(
            updates,
            update =>
                update.Kind == AnswerUpdateKind.CloudDelta &&
                update.Text.Contains("我会", StringComparison.Ordinal));
        Assert.DoesNotContain(
            updates,
            update => update.Kind == AnswerUpdateKind.LocalFallback);
    }

    [Fact]
    public async Task StreamAsync_UsesLocalCorrectionForOneKnowledgeSearch()
    {
        var repository = new RecordingRepository();
        var generator = new CapturingGenerator();
        var corrector = new StubCorrector(
            new DomainCorrectionResult(
                "circle注入是什么？",
                "SQL注入是什么？",
                CorrectionConfidence.High,
                CorrectionSource.Dictionary));
        var coordinator = new AnswerCoordinator(
            repository,
            () => generator,
            fallbackDelay: TimeSpan.FromMilliseconds(150),
            totalTimeout: TimeSpan.FromSeconds(2),
            questionCorrector: corrector);

        await foreach (var _ in coordinator.StreamAsync(
                           "circle注入是什么？",
                           CancellationToken.None))
        {
        }

        Assert.Equal(new[] { "SQL注入是什么？" }, repository.Queries);
        Assert.NotNull(generator.Request);
        Assert.Equal("circle注入是什么？", generator.Request.Question);
        Assert.Equal("SQL注入是什么？", generator.Request.Correction?.CandidateQuestion);
    }

    [Fact]
    public async Task StreamAsync_UsesRecentTurnForFollowUpAndRecordsFinalAnswer()
    {
        var repository = new RecordingRepository();
        var generator = new CapturingGenerator();
        var session = new InMemoryInterviewSession();
        session.Add(
            new InterviewTurn(
                DateTimeOffset.Parse("2026-07-24T10:00:00+08:00"),
                "SQL注入怎么防御？",
                "SQL注入怎么防御？",
                "我会使用参数化查询并限制数据库权限。"));
        var coordinator = new AnswerCoordinator(
            repository,
            () => generator,
            fallbackDelay: TimeSpan.FromMilliseconds(150),
            totalTimeout: TimeSpan.FromSeconds(2),
            interviewSession: session);

        await foreach (var _ in coordinator.StreamAsync(
                           "那它怎么绕过？",
                           CancellationToken.None))
        {
        }

        Assert.Equal(
            new[] { "SQL注入怎么防御？\n追问：那它怎么绕过？" },
            repository.Queries);
        Assert.Single(generator.Request!.Context!);
        Assert.Equal(
            "SQL注入怎么防御？",
            generator.Request.Context![0].CorrectedQuestion);
        var turns = session.Snapshot();
        Assert.Equal(2, turns.Count);
        Assert.Equal("那它怎么绕过？", turns[1].OriginalQuestion);
        Assert.Equal("我先回答 SQL 注入的核心原理。", turns[1].Answer);
    }

    [Fact]
    public async Task StreamAsync_OmitsContextForIndependentQuestion()
    {
        var repository = new RecordingRepository();
        var generator = new CapturingGenerator();
        var session = new InMemoryInterviewSession();
        session.Add(
            new InterviewTurn(
                DateTimeOffset.Parse("2026-07-24T10:00:00+08:00"),
                "SQL注入怎么防御？",
                "SQL注入怎么防御？",
                "我会使用参数化查询并限制数据库权限。"));
        var coordinator = new AnswerCoordinator(
            repository,
            () => generator,
            fallbackDelay: TimeSpan.FromMilliseconds(150),
            totalTimeout: TimeSpan.FromSeconds(2),
            interviewSession: session);

        await foreach (var _ in coordinator.StreamAsync(
                           "请介绍一下 Kerberos。",
                           CancellationToken.None))
        {
        }

        Assert.Equal(
            new[] { "请介绍一下 Kerberos。" },
            repository.Queries);
        Assert.Empty(generator.Request!.Context!);
    }

    [Fact]
    public async Task StreamAsync_StartsFallbackBeforeSynchronousModelWork()
    {
        using var gate = new ManualResetEventSlim();
        var coordinator = new AnswerCoordinator(
            new FakeRepository(),
            () => new SynchronouslyBlockedGenerator(gate),
            fallbackDelay: TimeSpan.FromMilliseconds(35),
            totalTimeout: TimeSpan.FromSeconds(2));
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(300);
                gate.Set();
            });
        AnswerUpdate? fallback = null;

        await foreach (var update in coordinator.StreamAsync(
                           "什么是命令执行？",
                           CancellationToken.None))
        {
            if (update.Kind == AnswerUpdateKind.LocalFallback)
            {
                fallback = update;
                break;
            }
        }

        await release;
        Assert.NotNull(fallback);
        Assert.True(
            fallback.Elapsed < TimeSpan.FromMilliseconds(150),
            $"本地兜底启动过晚：{fallback.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task StreamAsync_LimitsUntrustedModelOutput()
    {
        var session = new InMemoryInterviewSession();
        var coordinator = new AnswerCoordinator(
            new FakeRepository(),
            () => new OversizedGenerator(),
            fallbackDelay: TimeSpan.FromMilliseconds(150),
            totalTimeout: TimeSpan.FromSeconds(2),
            interviewSession: session);
        var cloudOutput = new System.Text.StringBuilder();

        await foreach (var update in coordinator.StreamAsync(
                           "请解释一下输出限制。",
                           CancellationToken.None))
        {
            if (update.Kind == AnswerUpdateKind.CloudDelta)
            {
                cloudOutput.Append(update.Text);
            }
        }

        Assert.Equal(
            InMemoryInterviewSession.MaximumAnswerCharacters,
            cloudOutput.Length);
        Assert.Equal(
            InMemoryInterviewSession.MaximumAnswerCharacters,
            Assert.Single(session.Snapshot()).Answer.Length);
    }

    private sealed class FakeRepository : IKnowledgeRepository
    {
        public Task<ImportResult> ImportDocxAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
            string question,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeHit>>(
                new[]
                {
                    new KnowledgeHit(
                        "id",
                        "Java 并发 · volatile",
                        "volatile 保证线程可见性和有序性。",
                        "面经.docx",
                        1),
                });
    }

    private sealed class HangingGenerator : IAnswerGenerator
    {
        public async IAsyncEnumerable<AnswerDelta> StreamAsync(
            AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class ImmediateGenerator : IAnswerGenerator
    {
        public async IAsyncEnumerable<AnswerDelta> StreamAsync(
            AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new AnswerDelta("我会先解释它的内存语义。");
        }
    }

    private sealed class OversizedGenerator : IAnswerGenerator
    {
        public async IAsyncEnumerable<AnswerDelta> StreamAsync(
            AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new AnswerDelta(
                new string(
                    '答',
                    InMemoryInterviewSession.MaximumAnswerCharacters + 1_000));
        }
    }

    private sealed class SynchronouslyBlockedGenerator(
        ManualResetEventSlim gate) : IAnswerGenerator
    {
        public async IAsyncEnumerable<AnswerDelta> StreamAsync(
            AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            gate.Wait(cancellationToken);
            await Task.Yield();
            yield break;
        }
    }

    private sealed class RecordingRepository : IKnowledgeRepository
    {
        public List<string> Queries { get; } = new();

        public Task<ImportResult> ImportDocxAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
            string question,
            int limit,
            CancellationToken cancellationToken)
        {
            Queries.Add(question);
            return Task.FromResult<IReadOnlyList<KnowledgeHit>>(
                new[]
                {
                    new KnowledgeHit(
                        "sql",
                        "SQL 注入原理",
                        "SQL 注入是把恶意 SQL 片段带入数据库查询。",
                        "面经.docx",
                        0.065),
                });
        }
    }

    private sealed class CapturingGenerator : IAnswerGenerator
    {
        public AnswerRequest? Request { get; private set; }

        public async IAsyncEnumerable<AnswerDelta> StreamAsync(
            AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Request = request;
            await Task.Yield();
            yield return new AnswerDelta("我先回答 SQL 注入的核心原理。");
        }
    }

    private sealed class StubCorrector(DomainCorrectionResult result)
        : IDomainQuestionCorrector
    {
        public DomainCorrectionResult Correct(string transcript) => result;
    }
}
