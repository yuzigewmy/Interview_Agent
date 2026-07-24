using InterviewAgent.Core.Sessions;

namespace InterviewAgent.Core.Tests;

public sealed class InMemoryInterviewSessionTests
{
    [Fact]
    public void Add_BoundsFullRecordsAndRecentContext()
    {
        var session = new InMemoryInterviewSession();
        for (var index = 0; index < 505; index++)
        {
            session.Add(CreateTurn(index, new string('答', 1_200)));
        }

        var records = session.Snapshot();
        var context = session.GetRecentContext();

        Assert.Equal(500, records.Count);
        Assert.Equal("原始问题 5", records[0].OriginalQuestion);
        Assert.InRange(context.Count, 1, 6);
        Assert.Equal("纠正问题 504", context[^1].CorrectedQuestion);
        Assert.True(
            context.Sum(turn =>
                turn.CorrectedQuestion.Length + turn.Answer.Length) <= 6_000);
    }

    [Fact]
    public void Clear_RemovesAllInMemoryConversationData()
    {
        var session = new InMemoryInterviewSession();
        session.Add(CreateTurn(1, "回答"));

        session.Clear();

        Assert.Equal(0, session.Count);
        Assert.Empty(session.Snapshot());
        Assert.Empty(session.GetRecentContext());
    }

    private static InterviewTurn CreateTurn(int index, string answer) =>
        new(
            DateTimeOffset.Parse("2026-07-24T10:00:00+08:00")
                .AddMinutes(index),
            $"原始问题 {index}",
            $"纠正问题 {index}",
            answer);
}
