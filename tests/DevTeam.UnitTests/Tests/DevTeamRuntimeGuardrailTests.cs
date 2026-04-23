namespace DevTeam.UnitTests.Tests;

internal static class DevTeamRuntimeGuardrailTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("AddGeneratedIssues_InfersFrontendDeveloper_ForBlazorWork", AddGeneratedIssues_InfersFrontendDeveloper_ForBlazorWork),
        new("CompleteRun_CreatesReviewerIssue_ForMeaningfulImplementation", CompleteRun_CreatesReviewerIssue_ForMeaningfulImplementation),
        new("CompleteRun_CreatesAuditorIssue_ForLargeChangeFootprint", CompleteRun_CreatesAuditorIssue_ForLargeChangeFootprint)
    ];

    private static Task AddGeneratedIssues_InfersFrontendDeveloper_ForBlazorWork()
    {
        var runtime = new DevTeamRuntime(new FakeSystemClock());
        var state = BuildStateWithRoles();

        var sourceIssue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Source issue",
            RoleSlug = "planner",
            Status = ItemStatus.Done,
            Priority = 80
        };
        state.Issues.Add(sourceIssue);

        var created = runtime.AddGeneratedIssues(
            state,
            sourceIssue.Id,
            [
                new GeneratedIssueProposal
                {
                    Title = "Implement Blazor dashboard component",
                    Detail = "Create the .razor component and update UI state bindings.",
                    RoleSlug = "developer",
                    Area = "ui",
                    Priority = 75
                }
            ]);

        var createdIssue = created.Single();
        Assert.That(createdIssue.RoleSlug == "frontend-developer",
            $"Expected frontend-developer specialization, got '{createdIssue.RoleSlug}'.");
        return Task.CompletedTask;
    }

    private static Task CompleteRun_CreatesReviewerIssue_ForMeaningfulImplementation()
    {
        var runtime = new DevTeamRuntime(new FakeSystemClock());
        var state = BuildStateWithRoles();

        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Implement workspace diagnostics panel",
            RoleSlug = "frontend-developer",
            Status = ItemStatus.InProgress,
            Priority = 82,
            ComplexityHint = 65,
            Area = "ui"
        };
        state.Issues.Add(issue);

        state.AgentRuns.Add(new AgentRun
        {
            Id = state.NextRunId++,
            IssueId = issue.Id,
            RoleSlug = issue.RoleSlug,
            Status = AgentRunStatus.Running,
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero)
        });

        runtime.CompleteRun(state, new CompleteRunRequest
        {
            RunId = 1,
            Outcome = "completed",
            Summary = "Implemented diagnostics panel.",
            ChangedPaths = ["src/A.cs", "src/B.cs", "src/C.cs"],
            CreatedIssueIds = [101, 102]
        });

        var reviewerIssue = state.Issues.SingleOrDefault(item =>
            item.RoleSlug == "reviewer" && item.DependsOnIssueIds.Contains(issue.Id));
        Assert.That(reviewerIssue is not null, "Expected runtime to create reviewer guardrail issue.");
        return Task.CompletedTask;
    }

    private static Task CompleteRun_CreatesAuditorIssue_ForLargeChangeFootprint()
    {
        var runtime = new DevTeamRuntime(new FakeSystemClock());
        var state = BuildStateWithRoles();

        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Implement cross-cutting telemetry cleanup",
            RoleSlug = "developer",
            Status = ItemStatus.InProgress,
            Priority = 88,
            Area = "runtime"
        };
        state.Issues.Add(issue);

        state.AgentRuns.Add(new AgentRun
        {
            Id = state.NextRunId++,
            IssueId = issue.Id,
            RoleSlug = issue.RoleSlug,
            Status = AgentRunStatus.Running,
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero)
        });

        runtime.CompleteRun(state, new CompleteRunRequest
        {
            RunId = 1,
            Outcome = "completed",
            Summary = "Telemetry cleanup complete.",
            ChangedPaths =
            [
                "src/A.cs", "src/B.cs", "src/C.cs", "src/D.cs", "src/E.cs",
                "src/F.cs", "src/G.cs", "src/H.cs", "src/I.cs"
            ]
        });

        var auditorIssue = state.Issues.SingleOrDefault(item => item.RoleSlug == "auditor" && item.Status == ItemStatus.Open);
        Assert.That(auditorIssue is not null, "Expected runtime to create auditor guardrail issue for large change footprint.");
        return Task.CompletedTask;
    }

    private static WorkspaceState BuildStateWithRoles() => new()
    {
        Phase = WorkflowPhase.Execution,
        Runtime = RuntimeConfiguration.CreateDefault(),
        Roles =
        [
            new RoleDefinition { Slug = "planner", Name = "Planner" },
            new RoleDefinition { Slug = "developer", Name = "Developer" },
            new RoleDefinition { Slug = "frontend-developer", Name = "Frontend Developer" },
            new RoleDefinition { Slug = "backend-developer", Name = "Backend Developer" },
            new RoleDefinition { Slug = "fullstack-developer", Name = "Fullstack Developer" },
            new RoleDefinition { Slug = "tester", Name = "Tester" },
            new RoleDefinition { Slug = "reviewer", Name = "Reviewer" },
            new RoleDefinition { Slug = "auditor", Name = "Auditor" }
        ]
    };
}
