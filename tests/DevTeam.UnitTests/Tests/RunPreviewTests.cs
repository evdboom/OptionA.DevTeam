namespace DevTeam.UnitTests.Tests;

internal static class RunPreviewTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildRunPreview_ReturnsReadyIssuesWithoutMutatingState", BuildRunPreview_ReturnsReadyIssuesWithoutMutatingState),
        new("BuildRunPreview_RespectsMaxSubagents", BuildRunPreview_RespectsMaxSubagents),
        new("BuildRunPreview_UsesFallbackWhenPremiumBudgetIsExhausted", BuildRunPreview_UsesFallbackWhenPremiumBudgetIsExhausted),
    ];

    private static Task BuildRunPreview_ReturnsReadyIssuesWithoutMutatingState()
    {
        var runtime = new DevTeamRuntime();
        var state = SeedData.BuildInitialState("C:\\test-repo", 25, 6);
        state.Phase = WorkflowPhase.Execution;
        runtime.AddIssue(state, "Implement feature", "", "developer", 100, null, []);

        var preview = runtime.BuildRunPreview(state, 1);

        Assert.That(preview.Count == 1, $"Expected one preview run, got {preview.Count}");
        Assert.That(preview[0].IssueId == 1, $"Expected preview for issue #1, got #{preview[0].IssueId}");
        Assert.That(state.Issues[0].Status == ItemStatus.Open, $"Preview should not mutate issue status, got {state.Issues[0].Status}");
        Assert.That(state.AgentRuns.Count == 0, $"Preview should not queue real runs, got {state.AgentRuns.Count}");
        Assert.That(state.Budget.CreditsCommitted == 0, $"Preview should not spend credits, got {state.Budget.CreditsCommitted}");
        return Task.CompletedTask;
    }

    private static Task BuildRunPreview_RespectsMaxSubagents()
    {
        var runtime = new DevTeamRuntime();
        var state = SeedData.BuildInitialState("C:\\test-repo", 25, 6);
        state.Phase = WorkflowPhase.Execution;
        runtime.AddIssue(state, "Implement feature A", "", "developer", 100, null, [], area: "api");
        runtime.AddIssue(state, "Implement feature B", "", "developer", 90, null, [], area: "ui");

        var preview = runtime.BuildRunPreview(state, 1);

        Assert.That(preview.Count == 1, $"Expected one preview run at max-subagents 1, got {preview.Count}");
        return Task.CompletedTask;
    }

    private static Task BuildRunPreview_UsesFallbackWhenPremiumBudgetIsExhausted()
    {
        var runtime = new DevTeamRuntime();
        var state = SeedData.BuildInitialState("C:\\test-repo", 25, 0);
        state.Phase = WorkflowPhase.Execution;
        runtime.AddIssue(state, "Review architecture", "", "reviewer", 100, null, []);

        var preview = runtime.BuildRunPreview(state, 1);

        Assert.That(preview.Count == 1, $"Expected one preview run, got {preview.Count}");
        Assert.That(preview[0].ModelName == "gpt-5.4", $"Expected fallback preview model gpt-5.4, got {preview[0].ModelName}");
        return Task.CompletedTask;
    }
}
