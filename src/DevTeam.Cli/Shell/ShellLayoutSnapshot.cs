using DevTeam.Core;

namespace DevTeam.Cli.Shell;

/// <summary>Active agent slots shown in the agent panel during execution.</summary>
internal sealed record AgentSlot(int RunId, int IssueId, string RoleSlug, string Title, AgentRunStatus Status);

/// <summary>Issue slots shown in the roadmap panel during execution.</summary>
internal sealed record RoadmapSlot(int Id, string Title, string RoleSlug, ItemStatus Status);

/// <summary>
/// Immutable snapshot of layout data consumed by the shell component.
/// Rebuilt on every state change so the component never touches the workspace directly.
/// </summary>
internal sealed record ShellLayoutSnapshot(
    WorkflowPhase Phase,
    bool ShowMiddleRow,
    IReadOnlyList<AgentSlot> Agents,
    IReadOnlyList<RoadmapSlot> Roadmap)
{
    public static readonly ShellLayoutSnapshot Empty =
        new(WorkflowPhase.Planning, false, [], []);
}
