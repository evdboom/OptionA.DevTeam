using DevTeam.Core;

namespace DevTeam.Cli.Shell;

/// <summary>Active agent slots shown in the agent panel during execution.</summary>
internal sealed record AgentSlot(int RunId, int IssueId, string RoleSlug, string Title, AgentRunStatus Status);

/// <summary>Issue slots used by the adventure map renderer.</summary>
internal sealed record RoadmapSlot(int Id, string Title, string RoleSlug, ItemStatus Status);

/// <summary>Pinned cycle status lines shown in the shell cycle panel.</summary>
internal sealed record CycleSlot(
    string RoleSlug,
    int? IssueId,
    string Title,
    TimeSpan Elapsed,
    bool IsRunning,
    bool IsCompleted,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Immutable snapshot of layout data consumed by the shell component.
/// Rebuilt on every state change so the component never touches the workspace directly.
/// </summary>
internal sealed record ShellLayoutSnapshot(
    WorkflowPhase Phase,
    IReadOnlyList<AgentSlot> Agents)
{
    public IReadOnlyList<CycleSlot> CurrentCycle { get; init; } = [];

    public static readonly ShellLayoutSnapshot Empty =
        new(WorkflowPhase.Planning, []);
}
