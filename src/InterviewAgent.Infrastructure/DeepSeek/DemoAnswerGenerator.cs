using System.Runtime.CompilerServices;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Answers;

namespace InterviewAgent.Infrastructure.DeepSeek;

public sealed class DemoAnswerGenerator : IAnswerGenerator
{
    public async IAsyncEnumerable<AnswerDelta> StreamAsync(
        AnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answer = BuildAnswer(request);
        foreach (var segment in Split(answer, 18))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(35, cancellationToken);
            yield return new AnswerDelta(segment);
        }
    }

    private static string BuildAnswer(AnswerRequest request)
    {
        var hit = request.Evidence.FirstOrDefault();
        if (hit is null)
        {
            return
                $"我的理解是，这个问题需要先明确核心定义，再说明工作机制和使用边界。" +
                $"针对“{request.Question.Trim()}”，我会结合实际场景从原理、取舍和落地方式三个方面回答。";
        }

        var content = hit.Content.Trim();
        if (content.Length > 420)
        {
            content = content[..420] + "……";
        }

        return
            $"我的理解是，这道题的核心可以先概括为：{content}\n\n" +
            "在实际回答时，我还会补充它的适用场景、局限以及与相近方案的取舍，" +
            "这样既说明原理，也能体现落地思考。";
    }

    private static IEnumerable<string> Split(string value, int size)
    {
        for (var index = 0; index < value.Length; index += size)
        {
            yield return value.Substring(index, Math.Min(size, value.Length - index));
        }
    }
}

