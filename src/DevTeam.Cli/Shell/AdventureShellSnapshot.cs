using DevTeam.Core;

namespace DevTeam.Cli.Shell;

internal sealed record AdventureRoleSlot(string RoleSlug, string DisplayName);

internal sealed record AdventureShellSnapshot(
    bool Enabled,
    WorkflowPhase Phase,
    IReadOnlyList<AdventureRoleSlot> Roles,
    IReadOnlyList<AgentSlot> Agents,
    IReadOnlyList<RoadmapSlot> Roadmap,
    IReadOnlyDictionary<string, string> SpeechBubbles);

internal readonly record struct AdventurePoint(int X, int Y);
