using System.Diagnostics;
using System.Runtime.CompilerServices;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Core.Orchestration;

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
}

