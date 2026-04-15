namespace DevTeam.UnitTests.Tests;

internal static class IssueServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("AddIssue_AssignsIncrementingId", AddIssue_AssignsIncrementingId),
        new("AddIssue_NormalizesRoleAlias", AddIssue_NormalizesRoleAlias),
        new("AddIssue_WithDependsOn_StoresDependencies", AddIssue_WithDependsOn_StoresDependencies),
        new("AdvancePipeline_SetsTimestamp_FromClock", AdvancePipeline_SetsTimestamp_FromClock),
        new("FindIssue_ReturnsNull_WhenNotFound", FindIssue_ReturnsNull_WhenNotFound),
        new("FindIssue_ReturnsIssue_WhenFound", FindIssue_ReturnsIssue_WhenFound),
        new("GetReadyIssues_ExcludesIssuesWithOpenDependencies", GetReadyIssues_ExcludesIssuesWithOpenDependencies),
        new("GetReadyIssues_IncludesIssueWithAllDependenciesDone", GetReadyIssues_IncludesIssueWithAllDependenciesDone),
        new("GetReadyIssues_RespectsMaxCount", GetReadyIssues_RespectsMaxCount),
    ];

    private static Task AddIssue_AssignsIncrementingId()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState();

        var i1 = svc.AddIssue(state, "First", "detail", "developer", 50, null, []);
        var i2 = svc.AddIssue(state, "Second", "detail", "developer", 50, null, []);

        Assert.That(i1.Id == 1, $"Expected id 1 but got {i1.Id}");
        Assert.That(i2.Id == 2, $"Expected id 2 but got {i2.Id}");
        return Task.CompletedTask;
    }

    private static Task AddIssue_NormalizesRoleAlias()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState
        {
            Roles = [new RoleDefinition { Slug = "developer", Name = "Developer" }]
        };

        var issue = svc.AddIssue(state, "Do work", "detail", "engineer", 50, null, []);

        Assert.That(issue.RoleSlug == "developer", $"Expected 'developer' but got '{issue.RoleSlug}'");
        return Task.CompletedTask;
    }

    private static Task AddIssue_WithDependsOn_StoresDependencies()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState();

        var dep = svc.AddIssue(state, "Dependency", "detail", "developer", 50, null, []);
        var issue = svc.AddIssue(state, "Dependent", "detail", "developer", 50, null, [dep.Id]);

        Assert.That(issue.DependsOnIssueIds.Contains(dep.Id),
            $"Expected DependsOnIssueIds to contain {dep.Id}");
        return Task.CompletedTask;
    }

    private static Task AdvancePipeline_SetsTimestamp_FromClock()
    {
        var clock = new FakeSystemClock();
        var svc = new IssueService(clock);
        var state = new WorkspaceState();
        state.Runtime.PipelineSchedulingEnabled = false;

        var issue = svc.AddIssue(state, "Feature", "detail", "developer", 50, null, []);
        var pipeline = new PipelineState
        {
            Id = state.NextPipelineId++,
            RoleSequence = ["developer", "tester"],
            IssueIds = [issue.Id],
            ActiveIssueId = issue.Id
        };
        state.Pipelines.Add(pipeline);
        issue.PipelineId = pipeline.Id;
        issue.PipelineStageIndex = 0;

        var advancedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        clock.UtcNow = advancedTime;

        svc.AdvancePipelineAfterCompletion(state, issue);

        Assert.That(pipeline.UpdatedAtUtc == advancedTime,
            $"Expected pipeline.UpdatedAtUtc == {advancedTime} but got {pipeline.UpdatedAtUtc}");
        return Task.CompletedTask;
    }

    private static Task FindIssue_ReturnsNull_WhenNotFound()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState();

        var result = svc.FindIssue(state, 999);

        Assert.That(result is null, "Expected null for non-existent issue");
        return Task.CompletedTask;
    }

    private static Task FindIssue_ReturnsIssue_WhenFound()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState();
        var issue = svc.AddIssue(state, "Find Me", "detail", "developer", 50, null, []);

        var found = svc.FindIssue(state, issue.Id);

        Assert.That(found is not null, "Expected to find the issue");
        Assert.That(found!.Title == "Find Me", $"Expected title 'Find Me' but got '{found.Title}'");
        return Task.CompletedTask;
    }

    private static Task GetReadyIssues_ExcludesIssuesWithOpenDependencies()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Execution };
        state.Runtime.PipelineSchedulingEnabled = false;

        var blocker = svc.AddIssue(state, "Blocker", "detail", "developer", 50, null, []);
        var dependent = svc.AddIssue(state, "Dependent", "detail", "developer", 50, null, [blocker.Id]);

        var ready = svc.GetReadyIssues(state, 10);

        Assert.That(ready.Count == 1, $"Expected 1 ready issue but got {ready.Count}");
        Assert.That(ready[0].Id == blocker.Id, $"Expected the blocker (#{blocker.Id}) to be ready");
        Assert.That(ready.All(i => i.Id != dependent.Id), "Dependent should not be ready");
        return Task.CompletedTask;
    }

    private static Task GetReadyIssues_IncludesIssueWithAllDependenciesDone()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Execution };
        state.Runtime.PipelineSchedulingEnabled = false;

        var done = svc.AddIssue(state, "Done Work", "detail", "developer", 50, null, []);
        done.Status = ItemStatus.Done;

        var ready = svc.AddIssue(state, "Ready Work", "detail", "developer", 50, null, [done.Id]);

        var result = svc.GetReadyIssues(state, 10);

        Assert.That(result.Count == 1, $"Expected 1 ready issue but got {result.Count}");
        Assert.That(result[0].Id == ready.Id, "Expected the dependent issue to be ready");
        return Task.CompletedTask;
    }

    private static Task GetReadyIssues_RespectsMaxCount()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Execution };
        state.Runtime.PipelineSchedulingEnabled = false;

        svc.AddIssue(state, "Issue 1", "detail", "developer", 50, null, []);
        svc.AddIssue(state, "Issue 2", "detail", "developer", 50, null, []);
        svc.AddIssue(state, "Issue 3", "detail", "developer", 50, null, []);

        var result = svc.GetReadyIssues(state, 1);

        Assert.That(result.Count == 1, $"Expected 1 issue but got {result.Count}");
        return Task.CompletedTask;
    }
}
