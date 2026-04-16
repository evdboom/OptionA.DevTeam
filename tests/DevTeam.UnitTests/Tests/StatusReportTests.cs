namespace DevTeam.UnitTests.Tests;

internal static class StatusReportTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildStatusReport_FlagsWaitingOnBlockingQuestion", BuildStatusReport_FlagsWaitingOnBlockingQuestion),
        new("BuildStatusReport_DoesNotMarkStalled_WhenWorkIsQueued", BuildStatusReport_DoesNotMarkStalled_WhenWorkIsQueued),
        new("BuildStatusReport_AggregatesRoleUsage", BuildStatusReport_AggregatesRoleUsage),
    ];

    private static Task BuildStatusReport_FlagsWaitingOnBlockingQuestion()
    {
        var clock = new FakeSystemClock
        {
            UtcNow = new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero)
        };
        var runtime = new DevTeamRuntime(clock: clock);
        var state = new WorkspaceState
        {
            Phase = WorkflowPhase.Execution
        };

        var question = runtime.AddQuestion(state, "Which auth flow should we use?", blocking: true);
        clock.Advance(TimeSpan.FromMinutes(90));

        var report = runtime.BuildStatusReport(state);

        Assert.That(report.LoopState == "waiting-for-user", $"Expected waiting-for-user but got {report.LoopState}");
        Assert.That(report.IsWaitingOnBlockingQuestion, "Expected report to mark the loop as waiting on a blocking question.");
        Assert.That(report.OpenQuestionAges.TryGetValue(question.Id, out var age), "Expected question age to be tracked.");
        Assert.That(age == TimeSpan.FromMinutes(90), $"Expected question age of 90 minutes but got {age}.");
        Assert.That(report.OldestBlockingQuestionAge == TimeSpan.FromMinutes(90),
            $"Expected oldest blocking question age of 90 minutes but got {report.OldestBlockingQuestionAge}.");
        return Task.CompletedTask;
    }

    private static Task BuildStatusReport_DoesNotMarkStalled_WhenWorkIsQueued()
    {
        var clock = new FakeSystemClock
        {
            UtcNow = new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero)
        };
        var runtime = new DevTeamRuntime(clock: clock);
        var state = new WorkspaceState
        {
            Phase = WorkflowPhase.Execution
        };

        _ = runtime.AddQuestion(state, "Pick a deployment target.", blocking: true);
        state.AgentRuns.Add(new AgentRun
        {
            Id = 1,
            IssueId = 42,
            RoleSlug = "developer",
            ModelName = "gpt-5.4",
            Status = AgentRunStatus.Queued,
            UpdatedAtUtc = clock.UtcNow
        });

        var report = runtime.BuildStatusReport(state);

        Assert.That(report.LoopState == "running", $"Expected running but got {report.LoopState}");
        Assert.That(!report.IsWaitingOnBlockingQuestion, "Expected queued work to suppress the stalled-on-question indicator.");
        Assert.That(report.OldestBlockingQuestionAge is null, "Expected no blocking question stall age while work is queued.");
        return Task.CompletedTask;
    }

    private static Task BuildStatusReport_AggregatesRoleUsage()
    {
        var runtime = new DevTeamRuntime();
        var state = new WorkspaceState
        {
            Phase = WorkflowPhase.Execution,
            AgentRuns =
            [
                new AgentRun
                {
                    Id = 1,
                    IssueId = 10,
                    RoleSlug = "developer",
                    Status = AgentRunStatus.Completed,
                    CreditsUsed = 2,
                    InputTokens = 1200,
                    OutputTokens = 300,
                    EstimatedCostUsd = 0.12
                },
                new AgentRun
                {
                    Id = 2,
                    IssueId = 11,
                    RoleSlug = "developer",
                    Status = AgentRunStatus.Queued,
                    CreditsUsed = 1
                },
                new AgentRun
                {
                    Id = 3,
                    IssueId = 12,
                    RoleSlug = "architect",
                    Status = AgentRunStatus.Completed,
                    CreditsUsed = 3,
                    PremiumCreditsUsed = 3,
                    InputTokens = 800,
                    OutputTokens = 200,
                    EstimatedCostUsd = 0.2
                }
            ]
        };

        var report = runtime.BuildStatusReport(state);
        var developerUsage = report.RoleUsage.Single(item => item.RoleSlug == "developer");
        var architectUsage = report.RoleUsage.Single(item => item.RoleSlug == "architect");

        Assert.That(developerUsage.RunCount == 2, $"Expected 2 developer runs but got {developerUsage.RunCount}");
        Assert.That(developerUsage.CompletedRunCount == 1, $"Expected 1 completed developer run but got {developerUsage.CompletedRunCount}");
        Assert.That(developerUsage.CreditsUsed == 3, $"Expected 3 developer credits but got {developerUsage.CreditsUsed}");
        Assert.That(developerUsage.InputTokens == 1200, $"Expected 1200 developer input tokens but got {developerUsage.InputTokens}");
        Assert.That(developerUsage.OutputTokens == 300, $"Expected 300 developer output tokens but got {developerUsage.OutputTokens}");
        Assert.That(architectUsage.PremiumCreditsUsed == 3, $"Expected 3 architect premium credits but got {architectUsage.PremiumCreditsUsed}");
        Assert.That(architectUsage.EstimatedCostUsd == 0.2, $"Expected architect USD estimate 0.2 but got {architectUsage.EstimatedCostUsd}");
        return Task.CompletedTask;
    }
}
