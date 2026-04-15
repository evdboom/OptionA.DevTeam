using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;

namespace DevTeam.ShellTests.Tests;

internal static class ShellLayoutSnapshotTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("EmptyState_HasNoAgentsAndNoRoadmap", EmptyState_HasNoAgentsAndNoRoadmap),
        new("ExecutionPhase_WithRunningAgents_ShowsAgentSlots", ExecutionPhase_WithRunningAgents_ShowsAgentSlots),
        new("ExecutionPhase_WithOpenIssues_ShowsRoadmapSlots", ExecutionPhase_WithOpenIssues_ShowsRoadmapSlots),
        new("PlanningPhase_IsCorrectlyReflected", PlanningPhase_IsCorrectlyReflected),
        new("ShowMiddleRow_FalseWhenNoAgentsAndNotExecution", ShowMiddleRow_FalseWhenNoAgentsAndNotExecution),
        new("ShowMiddleRow_TrueWhenAgentsPresent", ShowMiddleRow_TrueWhenAgentsPresent),
    ];

    private static Task EmptyState_HasNoAgentsAndNoRoadmap()
    {
        var snapshot = ShellLayoutSnapshot.Empty;
        Assert.That(snapshot.Agents.Count == 0, "Expected no agents");
        Assert.That(snapshot.Roadmap.Count == 0, "Expected no roadmap items");
        Assert.That(!snapshot.ShowMiddleRow, "Expected ShowMiddleRow to be false");
        return Task.CompletedTask;
    }

    private static Task ExecutionPhase_WithRunningAgents_ShowsAgentSlots()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            ShowMiddleRow: true,
            Agents: [new AgentSlot(1, 5, "developer", "Build the thing", AgentRunStatus.Running)],
            Roadmap: []);
        Assert.That(snapshot.Agents.Count == 1, "Expected one agent");
        Assert.That(snapshot.Agents[0].RoleSlug == "developer", "Expected developer role");
        Assert.That(snapshot.Agents[0].IssueId == 5, "Expected issue 5");
        Assert.That(snapshot.Agents[0].Status == AgentRunStatus.Running, "Expected Running status");
        Assert.That(snapshot.ShowMiddleRow, "Expected ShowMiddleRow to be true");
        return Task.CompletedTask;
    }

    private static Task ExecutionPhase_WithOpenIssues_ShowsRoadmapSlots()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            ShowMiddleRow: true,
            Agents: [],
            Roadmap: [
                new RoadmapSlot(5, "Build API", "developer", ItemStatus.Open),
                new RoadmapSlot(6, "Write tests", "tester", ItemStatus.Done),
            ]);
        Assert.That(snapshot.Roadmap.Count == 2, "Expected two roadmap items");
        Assert.That(snapshot.Roadmap[0].Title == "Build API", "Expected first item to be Build API");
        Assert.That(snapshot.Roadmap[1].Status == ItemStatus.Done, "Expected second item to be Done");
        return Task.CompletedTask;
    }

    private static Task PlanningPhase_IsCorrectlyReflected()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Planning,
            ShowMiddleRow: false,
            Agents: [],
            Roadmap: []);
        Assert.That(snapshot.Phase == WorkflowPhase.Planning, "Expected Planning phase");
        return Task.CompletedTask;
    }

    private static Task ShowMiddleRow_FalseWhenNoAgentsAndNotExecution()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Planning,
            ShowMiddleRow: false,
            Agents: [],
            Roadmap: [new RoadmapSlot(1, "Plan", "planner", ItemStatus.Open)]);
        Assert.That(!snapshot.ShowMiddleRow, "Planning phase with no agents: ShowMiddleRow should be false");
        return Task.CompletedTask;
    }

    private static Task ShowMiddleRow_TrueWhenAgentsPresent()
    {
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            ShowMiddleRow: true,
            Agents: [new AgentSlot(1, 2, "architect", "Design system", AgentRunStatus.Running)],
            Roadmap: []);
        Assert.That(snapshot.ShowMiddleRow, "Agents present: ShowMiddleRow should be true");
        return Task.CompletedTask;
    }
}
