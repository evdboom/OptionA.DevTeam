using DevTeam.Cli;
using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;

namespace DevTeam.ShellTests.Tests;

internal static class WorkflowGuideMarkupTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("PlanningGuide_PointsToPlan", PlanningGuide_PointsToPlan),
        new("PlanReviewGuide_MentionsPlainTextFeedback", PlanReviewGuide_MentionsPlainTextFeedback),
        new("ArchitectureGuide_PointsToRun", ArchitectureGuide_PointsToRun),
        new("ArchitectReviewGuide_PointsToApprove", ArchitectReviewGuide_PointsToApprove),
        new("ExecutionGuide_RecommendsSafeDefaults", ExecutionGuide_RecommendsSafeDefaults),
    ];

    private static Task PlanningGuide_PointsToPlan()
    {
        var state = UiHarness.BuildBaseState("C:\\temp");
        state.Phase = WorkflowPhase.Planning;

        var markup = ShellService.BuildWorkflowGuideMarkup(state, isLoopRunning: false, readyIssueCount: 0);

        Assert.That(markup is not null && markup.Contains("Step 1 of 3 - planning") && markup.Contains("/plan"),
            $"Expected planning guide to mention /plan, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task PlanReviewGuide_MentionsPlainTextFeedback()
    {
        var state = UiHarness.BuildBaseState("C:\\temp");
        state.Phase = WorkflowPhase.Planning;
        state.Issues.Add(new IssueItem { Id = 1, Title = "Plan work", RoleSlug = "planner", IsPlanningIssue = true, Status = ItemStatus.Done });

        var markup = ShellService.BuildWorkflowGuideMarkup(state, isLoopRunning: false, readyIssueCount: 0);

        Assert.That(markup is not null && markup.Contains("plan review") && markup.Contains("plain text"),
            $"Expected plan review guide to mention plain text feedback, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task ArchitectureGuide_PointsToRun()
    {
        var state = UiHarness.BuildBaseState("C:\\temp");
        state.Phase = WorkflowPhase.ArchitectPlanning;
        state.Issues.Add(new IssueItem { Id = 2, Title = "Design architecture", RoleSlug = "architect", Status = ItemStatus.Open });

        var markup = ShellService.BuildWorkflowGuideMarkup(state, isLoopRunning: false, readyIssueCount: 0);

        Assert.That(markup is not null && markup.Contains("Step 2 of 3 - architecture") && markup.Contains("/run"),
            $"Expected architecture guide to mention /run, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task ArchitectReviewGuide_PointsToApprove()
    {
        var state = UiHarness.BuildArchitectScenario("C:\\temp");
        state.Questions.Clear();

        var markup = ShellService.BuildWorkflowGuideMarkup(state, isLoopRunning: false, readyIssueCount: 0);

        Assert.That(markup is not null && markup.Contains("architect review") && markup.Contains("/approve"),
            $"Expected architect review guide to mention /approve, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task ExecutionGuide_RecommendsSafeDefaults()
    {
        var state = UiHarness.BuildBaseState("C:\\temp");
        state.Phase = WorkflowPhase.Execution;
        state.Issues.Add(new IssueItem { Id = 3, Title = "Implement feature", RoleSlug = "developer", Status = ItemStatus.Open });

        var markup = ShellService.BuildWorkflowGuideMarkup(state, isLoopRunning: false, readyIssueCount: 1);

        Assert.That(markup is not null && markup.Contains("Step 3 of 3 - execution") && markup.Contains("/max-subagents 1"),
            $"Expected execution guide to recommend /max-subagents 1, got: {markup}");
        return Task.CompletedTask;
    }
}
