namespace DevTeam.UnitTests.Tests;

internal static class IssueRefinementTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("NewIssue_DefaultsToPlannedRefinementState", NewIssue_DefaultsToPlannedRefinementState),
        new("NewIssue_HasEmptyFilesInScopeAndLinkedDecisionIds", NewIssue_HasEmptyFilesInScopeAndLinkedDecisionIds),
        new("GetIssue_ReturnsCorrectIssue", GetIssue_ReturnsCorrectIssue),
        new("GetIssue_Throws_WhenNotFound", GetIssue_Throws_WhenNotFound),
        new("GetDecisions_ReturnsOnlyRequestedIds", GetDecisions_ReturnsOnlyRequestedIds),
        new("GetDecisions_ReturnsEmpty_WhenNoIdsRequested", GetDecisions_ReturnsEmpty_WhenNoIdsRequested),
        new("GetDecisions_IgnoresMissingIds", GetDecisions_IgnoresMissingIds),
        new("IssueRefinementState_CanBeSetAndRead", IssueRefinementState_CanBeSetAndRead),
        new("FilesInScope_CanBePopulated", FilesInScope_CanBePopulated),
        new("LinkedDecisionIds_CanBePopulated", LinkedDecisionIds_CanBePopulated),
    ];

    private const string Detail = "detail";
    private const string Developer = "developer";

    private static Task NewIssue_DefaultsToPlannedRefinementState()
    {
        var state = new WorkspaceState();
        var issue = IssueService.AddIssue(state, "New feature", Detail, Developer, 50, null, []);

        Assert.That(issue.RefinementState == IssueRefinementState.Planned,
            $"Expected RefinementState=Planned but got {issue.RefinementState}");
        return Task.CompletedTask;
    }

    private static Task NewIssue_HasEmptyFilesInScopeAndLinkedDecisionIds()
    {
        var state = new WorkspaceState();
        var issue = IssueService.AddIssue(state, "New feature", Detail, Developer, 50, null, []);

        Assert.That(issue.FilesInScope.Count == 0,
            $"Expected empty FilesInScope but got {issue.FilesInScope.Count} entries");
        Assert.That(issue.LinkedDecisionIds.Count == 0,
            $"Expected empty LinkedDecisionIds but got {issue.LinkedDecisionIds.Count} entries");
        return Task.CompletedTask;
    }

    private static Task GetIssue_ReturnsCorrectIssue()
    {
        var state = new WorkspaceState();
        var issue = IssueService.AddIssue(state, "Target Issue", "some detail", Developer, 50, null, []);

        var found = DevTeamRuntime.GetIssue(state, issue.Id);

        Assert.That(found.Id == issue.Id, $"Expected id {issue.Id} but got {found.Id}");
        Assert.That(found.Title == "Target Issue", $"Expected title 'Target Issue' but got '{found.Title}'");
        Assert.That(found.Detail == "some detail", $"Expected detail 'some detail' but got '{found.Detail}'");
        return Task.CompletedTask;
    }

    private static Task GetIssue_Throws_WhenNotFound()
    {
        var state = new WorkspaceState();

        Assert.Throws<InvalidOperationException>(
            () => DevTeamRuntime.GetIssue(state, 999),
            "Expected InvalidOperationException for missing issue");
        return Task.CompletedTask;
    }

    private static Task GetDecisions_ReturnsOnlyRequestedIds()
    {
        var runtime = new DevTeamRuntime();
        var state = new WorkspaceState();

        var d1 = runtime.RecordDecision(state, "Decision One", "detail one", "test", null, null, null);
        var d2 = runtime.RecordDecision(state, "Decision Two", "detail two", "test", null, null, null);
        var d3 = runtime.RecordDecision(state, "Decision Three", "detail three", "test", null, null, null);

        var results = DevTeamRuntime.GetDecisions(state, [d1.Id, d3.Id]);

        Assert.That(results.Count == 2, $"Expected 2 decisions but got {results.Count}");
        Assert.That(results.Any(d => d.Id == d1.Id), "Expected Decision One in results");
        Assert.That(results.Any(d => d.Id == d3.Id), "Expected Decision Three in results");
        Assert.That(results.All(d => d.Id != d2.Id), "Expected Decision Two to be excluded");
        return Task.CompletedTask;
    }

    private static Task GetDecisions_ReturnsEmpty_WhenNoIdsRequested()
    {
        var runtime = new DevTeamRuntime();
        var state = new WorkspaceState();
        runtime.RecordDecision(state, "Some Decision", Detail, "test", null, null, null);

        var results = DevTeamRuntime.GetDecisions(state, []);

        Assert.That(results.Count == 0, $"Expected empty result but got {results.Count}");
        return Task.CompletedTask;
    }

    private static Task GetDecisions_IgnoresMissingIds()
    {
        var runtime = new DevTeamRuntime();
        var state = new WorkspaceState();
        var d1 = runtime.RecordDecision(state, "Real Decision", Detail, "test", null, null, null);

        // Request one real id and one non-existent id
        var results = DevTeamRuntime.GetDecisions(state, [d1.Id, 9999]);

        Assert.That(results.Count == 1, $"Expected 1 result but got {results.Count}");
        Assert.That(results[0].Id == d1.Id, $"Expected decision id {d1.Id} but got {results[0].Id}");
        return Task.CompletedTask;
    }

    private static Task IssueRefinementState_CanBeSetAndRead()
    {
        var state = new WorkspaceState();
        var issue = IssueService.AddIssue(state, "Feature", Detail, Developer, 50, null, []);

        issue.RefinementState = IssueRefinementState.ReadyToPickup;

        Assert.That(issue.RefinementState == IssueRefinementState.ReadyToPickup,
            $"Expected ReadyToPickup but got {issue.RefinementState}");
        return Task.CompletedTask;
    }

    private static Task FilesInScope_CanBePopulated()
    {
        var state = new WorkspaceState();
        var issue = IssueService.AddIssue(state, "Feature", Detail, Developer, 50, null, []);

        issue.FilesInScope.Add("src/Components/MyComponent.razor");
        issue.FilesInScope.Add("src/Components/MyComponent.razor.cs");
        issue.RefinementState = IssueRefinementState.ReadyToPickup;

        Assert.That(issue.FilesInScope.Count == 2, $"Expected 2 files but got {issue.FilesInScope.Count}");
        Assert.That(issue.FilesInScope.Contains("src/Components/MyComponent.razor"),
            "Expected MyComponent.razor in scope");
        return Task.CompletedTask;
    }

    private static Task LinkedDecisionIds_CanBePopulated()
    {
        var state = new WorkspaceState();
        var issue = IssueService.AddIssue(state, "Feature", Detail, Developer, 50, null, []);

        issue.LinkedDecisionIds.Add(7);
        issue.LinkedDecisionIds.Add(15);

        Assert.That(issue.LinkedDecisionIds.Count == 2, $"Expected 2 linked decisions but got {issue.LinkedDecisionIds.Count}");
        Assert.That(issue.LinkedDecisionIds.Contains(7), "Expected decision #7 linked");
        Assert.That(issue.LinkedDecisionIds.Contains(15), "Expected decision #15 linked");
        return Task.CompletedTask;
    }
}
