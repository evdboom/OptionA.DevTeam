namespace DevTeam.UnitTests.Tests;

internal static class QuestionServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("AddQuestion_AssignsIncrementingId", AddQuestion_AssignsIncrementingId),
        new("AddQuestion_DefaultsToBlocking_WhenFlagSet", AddQuestion_DefaultsToBlocking_WhenFlagSet),
        new("AddQuestion_RecordsCreatedTimestamp", AddQuestion_RecordsCreatedTimestamp),
        new("AnswerQuestion_SetsStatusToAnswered", AnswerQuestion_SetsStatusToAnswered),
        new("AnswerQuestion_Throws_WhenQuestionNotFound", AnswerQuestion_Throws_WhenQuestionNotFound),
        new("AddQuestion_NonBlocking_FlagRespected", AddQuestion_NonBlocking_FlagRespected),
    ];

    private static Task AddQuestion_AssignsIncrementingId()
    {
        var svc = new QuestionService(new FakeSystemClock());
        var state = new WorkspaceState();

        var q1 = svc.AddQuestion(state, "First question?", blocking: true);
        var q2 = svc.AddQuestion(state, "Second question?", blocking: true);

        Assert.That(q1.Id == 1, $"Expected id 1 but got {q1.Id}");
        Assert.That(q2.Id == 2, $"Expected id 2 but got {q2.Id}");
        return Task.CompletedTask;
    }

    private static Task AddQuestion_DefaultsToBlocking_WhenFlagSet()
    {
        var svc = new QuestionService(new FakeSystemClock());
        var state = new WorkspaceState();

        var question = svc.AddQuestion(state, "Is this blocking?", blocking: true);

        Assert.That(question.IsBlocking, "Expected IsBlocking to be true");
        Assert.That(question.Status == QuestionStatus.Open, "Expected status Open");
        return Task.CompletedTask;
    }

    private static Task AnswerQuestion_SetsStatusToAnswered()
    {
        var svc = new QuestionService(new FakeSystemClock());
        var state = new WorkspaceState();
        var question = svc.AddQuestion(state, "What is the plan?", blocking: true);

        svc.AnswerQuestion(state, question.Id, "The plan is X.");

        Assert.That(question.Status == QuestionStatus.Answered, $"Expected Answered but got {question.Status}");
        Assert.That(question.Answer == "The plan is X.", $"Expected answer 'The plan is X.' but got '{question.Answer}'");
        return Task.CompletedTask;
    }

    private static Task AddQuestion_RecordsCreatedTimestamp()
    {
        var clock = new FakeSystemClock
        {
            UtcNow = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero)
        };
        var svc = new QuestionService(clock);
        var state = new WorkspaceState();

        var question = svc.AddQuestion(state, "When was this asked?", blocking: true);

        Assert.That(question.CreatedAtUtc == clock.UtcNow,
            $"Expected CreatedAtUtc {clock.UtcNow:O} but got {question.CreatedAtUtc:O}");
        return Task.CompletedTask;
    }

    private static Task AnswerQuestion_Throws_WhenQuestionNotFound()
    {
        var svc = new QuestionService(new FakeSystemClock());
        var state = new WorkspaceState();

        Assert.Throws<InvalidOperationException>(
            () => svc.AnswerQuestion(state, 9999, "answer"),
            "Expected InvalidOperationException for missing question");
        return Task.CompletedTask;
    }

    private static Task AddQuestion_NonBlocking_FlagRespected()
    {
        var svc = new QuestionService(new FakeSystemClock());
        var state = new WorkspaceState();

        var question = svc.AddQuestion(state, "Nice to know?", blocking: false);

        Assert.That(!question.IsBlocking, "Expected IsBlocking to be false");
        Assert.That(question.Status == QuestionStatus.Open, "Expected status Open");
        return Task.CompletedTask;
    }
}
