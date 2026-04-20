using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;

namespace DevTeam.ShellTests.Tests;

internal static class ShellLayoutSnapshotTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("EmptyState_HasNoAgents", EmptyState_HasNoAgents),
        new("ExecutionPhase_WithRunningAgents_ShowsAgentSlots", ExecutionPhase_WithRunningAgents_ShowsAgentSlots),
        new("PlanningPhase_IsCorrectlyReflected", PlanningPhase_IsCorrectlyReflected),
    ];

    private static Task EmptyState_HasNoAgents()
    {
        var snapshot = ShellLayoutSnapshot.Empty;
        Assert.That(snapshot.Agents.Count == 0, "Expected no agents");
        return Task.CompletedTask;
    }

    private static Task ExecutionPhase_WithRunningAgents_ShowsAgentSlots()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            Agents: [new AgentSlot(1, 5, "developer", "Build the thing", AgentRunStatus.Running)]);
        Assert.That(snapshot.Agents.Count == 1, "Expected one agent");
        Assert.That(snapshot.Agents[0].RoleSlug == "developer", "Expected developer role");
        Assert.That(snapshot.Agents[0].IssueId == 5, "Expected issue 5");
        Assert.That(snapshot.Agents[0].Status == AgentRunStatus.Running, "Expected Running status");
        return Task.CompletedTask;
    }

    private static Task PlanningPhase_IsCorrectlyReflected()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Planning,
            Agents: []);
        Assert.That(snapshot.Phase == WorkflowPhase.Planning, "Expected Planning phase");
        return Task.CompletedTask;
    }
}
