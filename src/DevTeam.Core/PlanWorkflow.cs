namespace DevTeam.Core;

public enum PlanPreparationStatus
{
    Available,
    Generated,
    MissingGoal,
    Failed
}

public sealed class PlanPreparationResult
{
    public PlanPreparationStatus Status { get; init; }
    public LoopExecutionReport? Report { get; init; }
    public bool HasPlan => Status is PlanPreparationStatus.Available or PlanPreparationStatus.Generated;
}

public static class PlanWorkflow
{
    public static string GetPlanPath(WorkspaceStore store) => Path.Combine(store.WorkspacePath, "plan.md");

    public static bool HasPlan(WorkspaceStore store)
    {
        var path = GetPlanPath(store);
        return File.Exists(path) && !string.IsNullOrWhiteSpace(File.ReadAllText(path));
    }

    public static bool RequiresPlanningBeforeRun(WorkspaceState state, WorkspaceStore store) =>
        state.Phase == WorkflowPhase.Planning && !HasPlan(store);

    public static bool IsAwaitingApproval(WorkspaceState state, WorkspaceStore store) =>
        state.Phase == WorkflowPhase.Planning
        && HasPlan(store)
        && state.Issues.Any(item => item.IsPlanningIssue && item.Status == ItemStatus.Done);

    public static bool IsAwaitingArchitectApproval(WorkspaceState state) =>
        state.Phase == WorkflowPhase.ArchitectPlanning
        && state.Issues
            .Where(issue => string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase) && !issue.IsPlanningIssue)
            .All(issue => issue.Status == ItemStatus.Done)
        && state.Issues.Any(issue => string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase) && !issue.IsPlanningIssue);

    public static async Task<PlanPreparationResult> EnsurePlanAsync(
        WorkspaceStore store,
        WorkspaceState state,
        Func<WorkspaceState, Task<LoopExecutionReport>> runPlanningAsync)
    {
        if (HasPlan(store))
        {
            return new PlanPreparationResult
            {
                Status = PlanPreparationStatus.Available
            };
        }

        if (state.ActiveGoal is null)
        {
            return new PlanPreparationResult
            {
                Status = PlanPreparationStatus.MissingGoal
            };
        }

        var report = await runPlanningAsync(state);
        return new PlanPreparationResult
        {
            Status = HasPlan(store) ? PlanPreparationStatus.Generated : PlanPreparationStatus.Failed,
            Report = report
        };
    }
}
