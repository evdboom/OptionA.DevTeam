using System.Diagnostics.CodeAnalysis;

namespace DevTeam.TestInfrastructure;

public static class DevTeamRuntimeCompatExtensions
{
    [SuppressMessage("Major Code Smell", "S107", Justification = "Compatibility extension preserves pre-refactor call shape for tests.")]
    public static IssueItem AddIssue(
        this DevTeamRuntime runtime,
        WorkspaceState state,
        string title,
        string detail,
        string roleSlug,
        int priority,
        int? roadmapItemId,
        IEnumerable<int> dependsOn,
        string? area = null)
    {
        return runtime.AddIssue(
            state,
            new IssueRequest
            {
                Title = title,
                Detail = detail,
                RoleSlug = roleSlug,
                Priority = priority,
                RoadmapItemId = roadmapItemId,
                DependsOn = dependsOn,
                Area = area,
                FamilyKey = null,
                ParentIssueId = null,
                PipelineId = null,
                PipelineStageIndex = null,
                ComplexityHint = null
            });
    }
}
