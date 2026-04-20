namespace DevTeam.UnitTests.Tests;

internal static class IssueServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("AddIssue_AssignsIncrementingId", AddIssue_AssignsIncrementingId),
        new("AddIssue_NormalizesRoleAlias", AddIssue_NormalizesRoleAlias),
        new("AddIssue_WithDependsOn_StoresDependencies", AddIssue_WithDependsOn_StoresDependencies),
        new("EditIssue_UpdatesEditableFields", EditIssue_UpdatesEditableFields),
        new("EditIssue_RejectsPipelineTopologyChanges", EditIssue_RejectsPipelineTopologyChanges),
        new("EditIssue_RejectsQueuedOrRunningIssue", EditIssue_RejectsQueuedOrRunningIssue),
        new("AdvancePipeline_SetsTimestamp_FromClock", AdvancePipeline_SetsTimestamp_FromClock),
        new("AdvancePipeline_CreatesNextStageIssue_AndAdvancesActive", AdvancePipeline_CreatesNextStageIssue_AndAdvancesActive),
        new("AdvancePipeline_CompletesLastStage_MarksPipelineDone", AdvancePipeline_CompletesLastStage_MarksPipelineDone),
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

        var i1 = IssueService.AddIssue(state, "First", "detail", "developer", 50, null, []);
        var i2 = IssueService.AddIssue(state, "Second", "detail", "developer", 50, null, []);

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

        var issue = IssueService.AddIssue(state, "Do work", "detail", "engineer", 50, null, []);

        Assert.That(issue.RoleSlug == "developer", $"Expected 'developer' but got '{issue.RoleSlug}'");
        return Task.CompletedTask;
    }

    private static Task AddIssue_WithDependsOn_StoresDependencies()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState();

        var dep = IssueService.AddIssue(state, "Dependency", "detail", "developer", 50, null, []);
        var issue = IssueService.AddIssue(state, "Dependent", "detail", "developer", 50, null, [dep.Id]);

        Assert.That(issue.DependsOnIssueIds.Contains(dep.Id),
            $"Expected DependsOnIssueIds to contain {dep.Id}");
        return Task.CompletedTask;
    }

    private static Task EditIssue_UpdatesEditableFields()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState
        {
            Roles =
            [
                new RoleDefinition { Slug = "developer", Name = "Developer" },
                new RoleDefinition { Slug = "tester", Name = "Tester" }
            ],
            Phase = WorkflowPhase.Execution
        };

        var dep = IssueService.AddIssue(state, "Dependency", "detail", "developer", 40, null, []);
        var issue = IssueService.AddIssue(state, "Original title", "Original detail", "developer", 50, null, []);

        var edited = svc.EditIssue(state, new IssueEditRequest
        {
            IssueId = issue.Id,
            Title = "Updated title",
            Detail = "Updated detail",
            RoleSlug = "tester",
            Area = "UI Layer",
            Priority = 90,
            Status = "blocked",
            DependsOnIssueIds = [dep.Id],
            NotesToAppend = "Need design input."
        });

        Assert.That(edited.Title == "Updated title", $"Expected updated title but got '{edited.Title}'");
        Assert.That(edited.Detail == "Updated detail", $"Expected updated detail but got '{edited.Detail}'");
        Assert.That(edited.RoleSlug == "tester", $"Expected role 'tester' but got '{edited.RoleSlug}'");
        Assert.That(edited.Area == "ui-layer", $"Expected normalized area 'ui-layer' but got '{edited.Area}'");
        Assert.That(edited.Priority == 90, $"Expected priority 90 but got {edited.Priority}");
        Assert.That(edited.Status == ItemStatus.Blocked, $"Expected Blocked but got {edited.Status}");
        Assert.That(edited.DependsOnIssueIds.SequenceEqual([dep.Id]), "Expected dependency replacement.");
        Assert.That(edited.Notes.Contains("Need design input.", StringComparison.Ordinal), "Expected appended note.");
        return Task.CompletedTask;
    }

    private static Task EditIssue_RejectsPipelineTopologyChanges()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState
        {
            Roles =
            [
                new RoleDefinition { Slug = "developer", Name = "Developer" },
                new RoleDefinition { Slug = "tester", Name = "Tester" }
            ],
            Phase = WorkflowPhase.Execution
        };

        var issue = IssueService.AddIssue(state, "Feature", "detail", "developer", 50, null, []);
        svc.EnsurePipelineAssignments(state);

        Assert.Throws<InvalidOperationException>(
            () => svc.EditIssue(state, new IssueEditRequest
            {
                IssueId = issue.Id,
                RoleSlug = "tester"
            }),
            "Expected pipeline role edits to be rejected.");
        return Task.CompletedTask;
    }

    private static Task EditIssue_RejectsQueuedOrRunningIssue()
    {
        var svc = new IssueService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Execution };
        var issue = IssueService.AddIssue(state, "Feature", "detail", "developer", 50, null, []);
        state.AgentRuns.Add(new AgentRun
        {
            Id = 1,
            IssueId = issue.Id,
            Status = AgentRunStatus.Running
        });

        Assert.Throws<InvalidOperationException>(
            () => svc.EditIssue(state, new IssueEditRequest
            {
                IssueId = issue.Id,
                Priority = 80
            }),
            "Expected active-run edits to be rejected.");
        return Task.CompletedTask;
    }

    private static Task AdvancePipeline_SetsTimestamp_FromClock()
    {
        var clock = new FakeSystemClock();
        var svc = new IssueService(clock);
        var state = new WorkspaceState();
        state.Runtime.PipelineSchedulingEnabled = false;

        var issue = IssueService.AddIssue(state, "Feature", "detail", "developer", 50, null, []);
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

    private static Task AdvancePipeline_CreatesNextStageIssue_AndAdvancesActive()
    {
        var clock = new FakeSystemClock();
        var svc = new IssueService(clock);
        var state = new WorkspaceState();
        state.Runtime.PipelineSchedulingEnabled = false;

        var issue = IssueService.AddIssue(state, "Feature", "detail", "developer", 50, null, []);
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

        svc.AdvancePipelineAfterCompletion(state, issue);

        var nextIssue = state.Issues.FirstOrDefault(i => i.PipelineStageIndex == 1 && i.PipelineId == pipeline.Id);
        Assert.That(nextIssue is not null, "Expected a stage-1 issue to be created");
        Assert.That(nextIssue!.RoleSlug == "tester", $"Expected role 'tester' but got '{nextIssue.RoleSlug}'");
        Assert.That(pipeline.ActiveIssueId == nextIssue.Id,
            $"Expected pipeline.ActiveIssueId == {nextIssue.Id} but got {pipeline.ActiveIssueId}");
        Assert.That(pipeline.Status == PipelineStatus.Open,
            $"Expected pipeline status Open but got {pipeline.Status}");
        return Task.CompletedTask;
    }

    private static Task AdvancePipeline_CompletesLastStage_MarksPipelineDone()
    {
        var clock = new FakeSystemClock();
        var svc = new IssueService(clock);
        var state = new WorkspaceState();
        state.Runtime.PipelineSchedulingEnabled = false;

        // Single-role pipeline: completing the only stage finishes the pipeline
        var issue = IssueService.AddIssue(state, "Feature", "detail", "developer", 50, null, []);
        var pipeline = new PipelineState
        {
            Id = state.NextPipelineId++,
            RoleSequence = ["developer"],
            IssueIds = [issue.Id],
            ActiveIssueId = issue.Id
        };
        state.Pipelines.Add(pipeline);
        issue.PipelineId = pipeline.Id;
        issue.PipelineStageIndex = 0;

        svc.AdvancePipelineAfterCompletion(state, issue);

        Assert.That(pipeline.Status == PipelineStatus.Completed,
            $"Expected pipeline status Completed but got {pipeline.Status}");
        Assert.That(pipeline.ActiveIssueId is null,
            $"Expected pipeline.ActiveIssueId to be null but got {pipeline.ActiveIssueId}");
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
        var issue = IssueService.AddIssue(state, "Find Me", "detail", "developer", 50, null, []);

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

        var blocker = IssueService.AddIssue(state, "Blocker", "detail", "developer", 50, null, []);
        var dependent = IssueService.AddIssue(state, "Dependent", "detail", "developer", 50, null, [blocker.Id]);

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

        var done = IssueService.AddIssue(state, "Done Work", "detail", "developer", 50, null, []);
        done.Status = ItemStatus.Done;

        var ready = IssueService.AddIssue(state, "Ready Work", "detail", "developer", 50, null, [done.Id]);

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

        IssueService.AddIssue(state, "Issue 1", "detail", "developer", 50, null, []);
        IssueService.AddIssue(state, "Issue 2", "detail", "developer", 50, null, []);
        IssueService.AddIssue(state, "Issue 3", "detail", "developer", 50, null, []);

        var result = svc.GetReadyIssues(state, 1);

        Assert.That(result.Count == 1, $"Expected 1 issue but got {result.Count}");
        return Task.CompletedTask;
    }
}
