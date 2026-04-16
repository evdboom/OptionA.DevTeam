namespace DevTeam.Core;

public sealed class QuestionService : IQuestionService
{
    private readonly ISystemClock _clock;

    public QuestionService(ISystemClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
    }

    public QuestionItem AddQuestion(WorkspaceState state, string text, bool blocking)
    {
        var normalized = text.Trim();
        var existing = state.Questions.FirstOrDefault(item =>
            item.Status == QuestionStatus.Open
            && item.IsBlocking == blocking
            && string.Equals(item.Text, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var question = new QuestionItem
        {
            Id = state.NextQuestionId++,
            Text = normalized,
            IsBlocking = blocking,
            CreatedAtUtc = _clock.UtcNow
        };
        state.Questions.Add(question);
        return question;
    }

    public void AnswerQuestion(WorkspaceState state, int questionId, string answer)
    {
        var question = state.Questions.FirstOrDefault(item => item.Id == questionId)
            ?? throw new InvalidOperationException($"Question #{questionId} was not found.");
        question.Answer = answer.Trim();
        question.Status = QuestionStatus.Answered;
        RecordDecision(
            state,
            $"Answered question #{question.Id}",
            $"{question.Text}\n\nAnswer: {question.Answer}",
            "question");

        if (!state.Questions.Any(item => item.Status == QuestionStatus.Open && item.IsBlocking))
        {
            foreach (var issue in state.Issues.Where(item => item.Status == ItemStatus.Blocked))
            {
                issue.Status = ItemStatus.Open;
            }
        }
    }

    public IReadOnlyList<QuestionItem> AddQuestions(WorkspaceState state, IEnumerable<ProposedQuestion> questions)
    {
        return questions
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => AddQuestion(state, item.Text, item.IsBlocking))
            .ToList();
    }

    private void RecordDecision(WorkspaceState state, string title, string detail, string source)
    {
        state.Decisions.Add(new DecisionRecord
        {
            Id = state.NextDecisionId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Source = source.Trim(),
            CreatedAtUtc = _clock.UtcNow
        });
    }
}
