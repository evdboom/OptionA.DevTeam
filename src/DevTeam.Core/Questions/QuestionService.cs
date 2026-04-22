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
        var created = new List<QuestionItem>();
        foreach (var candidate in questions.Where(item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            if (ShouldAutoResolveRuntimeManagedQuestion(candidate))
            {
                RecordDecision(
                    state,
                    "Auto-resolved runtime-managed question",
                    $"Question: {candidate.Text.Trim()}\n\nResolution: Runtime policy owns this operational decision and will continue without user input.",
                    "runtime-policy");
                continue;
            }

            created.Add(AddQuestion(state, candidate.Text, candidate.IsBlocking));
        }

        return created;
    }

    private static bool ShouldAutoResolveRuntimeManagedQuestion(ProposedQuestion question)
    {
        if (question.IsBlocking)
        {
            return false;
        }

        var text = question.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ContainsAny(text,
            "timeout",
            "retry",
            "batch",
            "pair",
            "sequential",
            "merge risk",
            "subagent",
            "split",
            "dependency",
            "depends on",
            "close #",
            "proactively close",
            "save the credit",
            "credit",
            "budget",
            "phase",
            "pipeline",
            "queue",
            "scheduler");
    }

    private static bool ContainsAny(string source, params string[] needles) =>
        needles.Any(needle => source.Contains(needle, StringComparison.OrdinalIgnoreCase));

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
