namespace DevTeam.Core;

public interface IPlanningService
{
    void ApprovePlan(WorkspaceState state, string note);
    void ApproveArchitectPlan(WorkspaceState state, string note);
    void RecordPlanningFeedback(WorkspaceState state, string feedback);
    IReadOnlyList<string> EnsureBootstrapPlan(WorkspaceState state);
    void EnsureApprovedPlanningIssuesClosed(WorkspaceState state);
}
