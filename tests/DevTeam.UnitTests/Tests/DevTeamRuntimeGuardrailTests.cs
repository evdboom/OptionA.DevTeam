namespace DevTeam.UnitTests.Tests;

internal static class DevTeamRuntimeGuardrailTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("AddGeneratedIssues_InfersFrontendDeveloper_ForBlazorWork", AddGeneratedIssues_InfersFrontendDeveloper_ForBlazorWork),
        new("AddGeneratedIssues_DoesNotSpecializeAliasRole", AddGeneratedIssues_DoesNotSpecializeAliasRole),
        new("CompleteRun_CreatesReviewerIssue_ForMeaningfulImplementation", CompleteRun_CreatesReviewerIssue_ForMeaningfulImplementation),
        new("CompleteRun_CreatesReviewerIssue_OnCadenceWithoutDiff", CompleteRun_CreatesReviewerIssue_OnCadenceWithoutDiff),
        new("CompleteRun_CreatesAuditorIssue_ForLargeChangeFootprint", CompleteRun_CreatesAuditorIssue_ForLargeChangeFootprint),
        new("CompleteRun_CreatesAuditorIssue_OnCadenceWithoutLargeDiff", CompleteRun_CreatesAuditorIssue_OnCadenceWithoutLargeDiff),
        new("CompleteRun_CreatesAuditorIssue_WhenOtherOpenRolesExist", CompleteRun_CreatesAuditorIssue_WhenOtherOpenRolesExist),
        new("IsScopeComplete_ReturnsFalse_WhenPlannedIssuesStillOpen", IsScopeComplete_ReturnsFalse_WhenPlannedIssuesStillOpen),
        new("IsScopeComplete_ReturnsTrue_WhenAllPipelineIssuesDone_DriftRemaining", IsScopeComplete_ReturnsTrue_WhenAllPipelineIssuesDone_DriftRemaining),
        new("IsScopeComplete_ReturnsFalse_WhenNotInExecutionPhase", IsScopeComplete_ReturnsFalse_WhenNotInExecutionPhase),
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

    private static Task AddGeneratedIssues_DoesNotSpecializeAliasRole()
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
                    Title = "Build game loop",
                    Detail = "Implement the frame loop.",
                    RoleSlug = "engineer",
                    Priority = 75
                }
            ]);

        var createdIssue = created.Single();
        Assert.That(createdIssue.RoleSlug == "developer",
            $"Expected alias to normalize to developer, got '{createdIssue.RoleSlug}'.");
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

    private static Task CompleteRun_CreatesReviewerIssue_OnCadenceWithoutDiff()
    {
        var runtime = new DevTeamRuntime(new FakeSystemClock());
        var state = BuildStateWithRoles();

        var issue1 = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Implement endpoint one",
            RoleSlug = "developer",
            Status = ItemStatus.InProgress,
            Priority = 70,
            Area = "api"
        };
        var issue2 = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Implement endpoint two",
            RoleSlug = "developer",
            Status = ItemStatus.InProgress,
            Priority = 69,
            Area = "api"
        };
        state.Issues.Add(issue1);
        state.Issues.Add(issue2);

        state.AgentRuns.Add(new AgentRun
        {
            Id = state.NextRunId++,
            IssueId = issue1.Id,
            RoleSlug = issue1.RoleSlug,
            Status = AgentRunStatus.Running,
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero)
        });
        runtime.CompleteRun(state, new CompleteRunRequest
        {
            RunId = 1,
            Outcome = "completed",
            Summary = "Endpoint one complete.",
            ChangedPaths = []
        });

        state.AgentRuns.Add(new AgentRun
        {
            Id = state.NextRunId++,
            IssueId = issue2.Id,
            RoleSlug = issue2.RoleSlug,
            Status = AgentRunStatus.Running,
            UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 2, 0, TimeSpan.Zero)
        });
        runtime.CompleteRun(state, new CompleteRunRequest
        {
            RunId = 2,
            Outcome = "completed",
            Summary = "Endpoint two complete.",
            ChangedPaths = []
        });

        var reviewerIssue = state.Issues.SingleOrDefault(item =>
            item.RoleSlug == "reviewer"
            && item.Status == ItemStatus.Open
            && item.Title.Contains("Review", StringComparison.OrdinalIgnoreCase));
        Assert.That(reviewerIssue is not null, "Expected cadence-based reviewer issue when multiple implementation runs completed without diffs.");
        return Task.CompletedTask;
    }

    private static Task CompleteRun_CreatesAuditorIssue_OnCadenceWithoutLargeDiff()
    {
        var runtime = new DevTeamRuntime(new FakeSystemClock());
        var state = BuildStateWithRoles();

        var issue1 = new IssueItem { Id = state.NextIssueId++, Title = "Implement feature A", RoleSlug = "developer", Status = ItemStatus.InProgress, Priority = 60 };
        var issue2 = new IssueItem { Id = state.NextIssueId++, Title = "Implement feature B", RoleSlug = "developer", Status = ItemStatus.InProgress, Priority = 59 };
        var issue3 = new IssueItem { Id = state.NextIssueId++, Title = "Implement feature C", RoleSlug = "developer", Status = ItemStatus.InProgress, Priority = 58 };
        state.Issues.Add(issue1);
        state.Issues.Add(issue2);
        state.Issues.Add(issue3);

        state.AgentRuns.Add(new AgentRun { Id = state.NextRunId++, IssueId = issue1.Id, RoleSlug = issue1.RoleSlug, Status = AgentRunStatus.Running, UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero) });
        runtime.CompleteRun(state, new CompleteRunRequest { RunId = 1, Outcome = "completed", Summary = "A done", ChangedPaths = [] });

        state.AgentRuns.Add(new AgentRun { Id = state.NextRunId++, IssueId = issue2.Id, RoleSlug = issue2.RoleSlug, Status = AgentRunStatus.Running, UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 3, 0, TimeSpan.Zero) });
        runtime.CompleteRun(state, new CompleteRunRequest { RunId = 2, Outcome = "completed", Summary = "B done", ChangedPaths = [] });

        state.AgentRuns.Add(new AgentRun { Id = state.NextRunId++, IssueId = issue3.Id, RoleSlug = issue3.RoleSlug, Status = AgentRunStatus.Running, UpdatedAtUtc = new DateTimeOffset(2026, 4, 23, 10, 6, 0, TimeSpan.Zero) });
        runtime.CompleteRun(state, new CompleteRunRequest { RunId = 3, Outcome = "completed", Summary = "C done", ChangedPaths = [] });

        var auditorIssue = state.Issues.SingleOrDefault(item =>
            item.RoleSlug == "auditor"
            && item.Status == ItemStatus.Open
            && item.Title.Contains("Audit recent execution drift", StringComparison.OrdinalIgnoreCase));
        Assert.That(auditorIssue is not null, "Expected cadence-based auditor issue after three implementation completions.");
        return Task.CompletedTask;
    }

    private static Task CompleteRun_CreatesAuditorIssue_WhenOtherOpenRolesExist()
    {
        var runtime = new DevTeamRuntime(new FakeSystemClock());
        var state = BuildStateWithRoles();

        state.Issues.Add(new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Unrelated open work",
            RoleSlug = "developer",
            Status = ItemStatus.Open,
            Priority = 40
        });

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
        Assert.That(auditorIssue is not null, "Expected runtime to create auditor issue despite unrelated open issues.");
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

    private static Task IsScopeComplete_ReturnsFalse_WhenPlannedIssuesStillOpen()
    {
        var state = BuildStateWithRoles();
        state.Issues.Add(new IssueItem { Id = 1, Title = "Implement A", RoleSlug = "developer", PipelineId = 1, Status = ItemStatus.Open });
        state.Issues.Add(new IssueItem { Id = 2, Title = "Implement B", RoleSlug = "developer", PipelineId = 2, Status = ItemStatus.Done });

        var result = DevTeamRuntime.IsScopeComplete(state);

        Assert.That(!result, "Expected IsScopeComplete=false when at least one pipeline issue is still open.");
        return Task.CompletedTask;
    }

    private static Task IsScopeComplete_ReturnsTrue_WhenAllPipelineIssuesDone_DriftRemaining()
    {
        var state = BuildStateWithRoles();
        // Planned pipeline issues — all done
        state.Issues.Add(new IssueItem { Id = 1, Title = "Implement A", RoleSlug = "developer", PipelineId = 1, Status = ItemStatus.Done });
        state.Issues.Add(new IssueItem { Id = 2, Title = "Implement B", RoleSlug = "developer", PipelineId = 2, Status = ItemStatus.Done });
        // Drift/audit issue — still open; should not block scope-complete
        state.Issues.Add(new IssueItem { Id = 3, Title = "Audit recent drift", RoleSlug = "auditor", Area = "repo-audit", FamilyKey = "repo-audit", Status = ItemStatus.Open });

        var result = DevTeamRuntime.IsScopeComplete(state);

        Assert.That(result, "Expected IsScopeComplete=true when all pipeline issues are done and only audit drift remains.");
        return Task.CompletedTask;
    }

    private static Task IsScopeComplete_ReturnsFalse_WhenNotInExecutionPhase()
    {
        var state = BuildStateWithRoles();
        state.Phase = WorkflowPhase.Planning;
        state.Issues.Add(new IssueItem { Id = 1, Title = "Plan work", RoleSlug = "planner", PipelineId = 1, Status = ItemStatus.Done });

        var result = DevTeamRuntime.IsScopeComplete(state);

        Assert.That(!result, "Expected IsScopeComplete=false outside of Execution phase.");
        return Task.CompletedTask;
    }
}
