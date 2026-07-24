namespace InterviewAgent.Core.Sessions;

public sealed record InterviewTurn(
    DateTimeOffset RecordedAt,
    string OriginalQuestion,
    string CorrectedQuestion,
    string Answer);

public interface IInterviewSession
{
    int Count { get; }

    void Add(InterviewTurn turn);

    IReadOnlyList<InterviewTurn> Snapshot();

    IReadOnlyList<InterviewTurn> GetRecentContext();

    void Clear();
}

public sealed class InMemoryInterviewSession : IInterviewSession
{
    public const int MaximumRecords = 500;
    public const int MaximumContextTurns = 6;
    public const int MaximumContextCharacters = 6_000;
    public const int MaximumAnswerCharacters = 20_000;
    private const int MaximumQuestionCharacters = 2_000;
    private readonly object _gate = new();
    private readonly List<InterviewTurn> _turns = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _turns.Count;
            }
        }
    }

    public void Add(InterviewTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var normalized = new InterviewTurn(
            turn.RecordedAt,
            NormalizeRequired(
                turn.OriginalQuestion,
                MaximumQuestionCharacters,
                nameof(turn.OriginalQuestion)),
            NormalizeRequired(
                turn.CorrectedQuestion,
                MaximumQuestionCharacters,
                nameof(turn.CorrectedQuestion)),
            NormalizeRequired(
                turn.Answer,
                MaximumAnswerCharacters,
                nameof(turn.Answer)));

        lock (_gate)
        {
            _turns.Add(normalized);
            if (_turns.Count > MaximumRecords)
            {
                _turns.RemoveRange(0, _turns.Count - MaximumRecords);
            }
        }
    }

    public IReadOnlyList<InterviewTurn> Snapshot()
    {
        lock (_gate)
        {
            return _turns.ToArray();
        }
    }

    public IReadOnlyList<InterviewTurn> GetRecentContext()
    {
        InterviewTurn[] recent;
        lock (_gate)
        {
            recent = _turns
                .TakeLast(MaximumContextTurns)
                .ToArray();
        }

        var remaining = MaximumContextCharacters;
        var result = new List<InterviewTurn>(recent.Length);
        for (var index = recent.Length - 1; index >= 0; index--)
        {
            var turn = recent[index];
            if (remaining <= 0)
            {
                break;
            }

            var question = Truncate(turn.CorrectedQuestion, remaining);
            remaining -= question.Length;
            var answer = Truncate(turn.Answer, remaining);
            remaining -= answer.Length;
            if (question.Length == 0 && answer.Length == 0)
            {
                break;
            }

            result.Insert(
                0,
                turn with
                {
                    OriginalQuestion = question,
                    CorrectedQuestion = question,
                    Answer = answer,
                });
        }

        return result;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _turns.Clear();
        }
    }

    private static string NormalizeRequired(
        string value,
        int maximumCharacters,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        return Truncate(normalized, maximumCharacters);
    }

    private static string Truncate(string value, int maximumCharacters)
    {
        if (maximumCharacters <= 0)
        {
            return string.Empty;
        }

        return value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters];
    }
}
