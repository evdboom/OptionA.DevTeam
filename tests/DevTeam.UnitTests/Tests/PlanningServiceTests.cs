namespace DevTeam.UnitTests.Tests;

internal static class PlanningServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("ApprovePlan_TransitionsToExecution_WhenNoArchitectWork", ApprovePlan_TransitionsToExecution_WhenNoArchitectWork),
        new("ApprovePlan_TransitionsToArchitectPlanning_WhenArchitectIssuesExist", ApprovePlan_TransitionsToArchitectPlanning_WhenArchitectIssuesExist),
        new("ApproveArchitectPlan_TransitionsToExecution", ApproveArchitectPlan_TransitionsToExecution),
        new("RecordPlanningFeedback_ThrowsOnEmptyFeedback", RecordPlanningFeedback_ThrowsOnEmptyFeedback),
    ];

    private static Task ApprovePlan_TransitionsToExecution_WhenNoArchitectWork()
    {
        var svc = new PlanningService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Planning };
        // No architect issues → should go directly to Execution

        svc.ApprovePlan(state, "Looks good.");

        Assert.That(state.Phase == WorkflowPhase.Execution,
            $"Expected Execution phase but got {state.Phase}");
        Assert.That(state.Decisions.Count == 1, "Expected one decision to be recorded");
        return Task.CompletedTask;
    }

    private static Task ApprovePlan_TransitionsToArchitectPlanning_WhenArchitectIssuesExist()
    {
        var svc = new PlanningService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Planning };
        state.Issues.Add(new IssueItem
        {
            Id = 1,
            Title = "Design the architecture",
            RoleSlug = "architect",
            IsPlanningIssue = false,
            Status = ItemStatus.Open
        });

        svc.ApprovePlan(state, "");

        Assert.That(state.Phase == WorkflowPhase.ArchitectPlanning,
            $"Expected ArchitectPlanning but got {state.Phase}");
        return Task.CompletedTask;
    }

    private static Task ApproveArchitectPlan_TransitionsToExecution()
    {
        var svc = new PlanningService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.ArchitectPlanning };

        svc.ApproveArchitectPlan(state, "Architecture approved.");

        Assert.That(state.Phase == WorkflowPhase.Execution,
            $"Expected Execution but got {state.Phase}");
        return Task.CompletedTask;
    }

    private static Task RecordPlanningFeedback_ThrowsOnEmptyFeedback()
    {
        var svc = new PlanningService(new FakeSystemClock());
        var state = new WorkspaceState { Phase = WorkflowPhase.Planning };

        Assert.Throws<InvalidOperationException>(
            () => svc.RecordPlanningFeedback(state, "   "),
            "Expected InvalidOperationException for empty feedback");
        return Task.CompletedTask;
    }
}
