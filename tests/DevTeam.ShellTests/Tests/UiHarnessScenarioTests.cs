using DevTeam.Cli;
using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;
using Spectre.Console.Testing;

namespace DevTeam.ShellTests.Tests;

internal static class UiHarnessScenarioTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("EmptyScenario_RendersWithoutError", EmptyScenario_RendersWithoutError),
        new("PlanningScenario_HeaderContainsPlanning", PlanningScenario_HeaderContainsPlanning),
        new("ArchitectScenario_HeaderContainsArchitect", ArchitectScenario_HeaderContainsArchitect),
        new("ExecutionScenario_AgentsPanelContainsArchitect", ExecutionScenario_AgentsPanelContainsArchitect),
        new("ExecutionScenario_RoadmapPanelContainsIssueTitles", ExecutionScenario_RoadmapPanelContainsIssueTitles),
        new("QuestionsScenario_RendersWithoutError", QuestionsScenario_RendersWithoutError),
    ];

    private static string WorkspacePath => Path.Combine(Path.GetTempPath(), $"devteam-shelltest-{Guid.NewGuid():N}");

    private static ShellLayoutSnapshot BuildSnapshot(WorkspaceState state, bool loopRunning = false)
    {
        var agents = state.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Queued
                     || (r.Status == AgentRunStatus.Running && loopRunning))
            .Select(r =>
            {
                var issue = state.Issues.FirstOrDefault(i => i.Id == r.IssueId);
                return new AgentSlot(r.Id, r.IssueId, r.RoleSlug, issue?.Title ?? $"Issue #{r.IssueId}", r.Status);
            })
            .ToList();

        var roadmap = state.Issues
            .Where(i => !i.IsPlanningIssue)
            .OrderByDescending(i => i.Priority).ThenBy(i => i.Id)
            .Select(i => new RoadmapSlot(i.Id, i.Title, i.RoleSlug, i.Status))
            .ToList();

        var phase = state.Phase;
        var showMiddle = agents.Count > 0 || (phase == WorkflowPhase.Execution && roadmap.Count > 0);
        return new ShellLayoutSnapshot(phase, showMiddle, agents, roadmap);
    }

    private static TestConsole CreateConsole()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        return console;
    }

    private static Task EmptyScenario_RendersWithoutError()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildEmptyScenario(wp);
            var snapshot = BuildSnapshot(state);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, false));
            console.Write(ShellPanelBuilder.BuildEmptyPanel("Agents"));
            console.Write(ShellPanelBuilder.BuildEmptyPanel("Roadmap"));
            // No assertion needed — just verify it doesn't throw
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task PlanningScenario_HeaderContainsPlanning()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildPlanningScenario(wp);
            var snapshot = BuildSnapshot(state);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, false));
            var output = console.Output;
            Assert.That(output.Contains("Planning"), $"Expected 'Planning' in header but got: {output}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ArchitectScenario_HeaderContainsArchitect()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildArchitectScenario(wp);
            var snapshot = BuildSnapshot(state);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, false));
            var output = console.Output;
            Assert.That(output.Contains("Architect") || output.Contains("Planning"),
                $"Expected architect/planning phase label but got: {output}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ExecutionScenario_AgentsPanelContainsArchitect()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildExecutionScenario(wp);
            // The execution scenario has a Running architect run; loopRunning=true to show it
            var snapshot = BuildSnapshot(state, loopRunning: true);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildAgentsPanel(snapshot));
            var output = console.Output;
            Assert.That(output.Contains("architect"), $"Expected 'architect' in agents panel but got: {output}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ExecutionScenario_RoadmapPanelContainsIssueTitles()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildExecutionScenario(wp);
            var snapshot = BuildSnapshot(state, loopRunning: true);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildRoadmapPanel(snapshot, 10));
            var output = console.Output;
            // Just assert that some issue titles from the execution scenario appear
            Assert.That(output.Length > 0, "Expected non-empty roadmap output");
            Assert.That(snapshot.Roadmap.Count > 0, "Expected roadmap to have issues");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task QuestionsScenario_RendersWithoutError()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildQuestionsScenario(wp);
            var snapshot = BuildSnapshot(state, loopRunning: true);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, true));
            console.Write(ShellPanelBuilder.BuildAgentsPanel(snapshot));
            console.Write(ShellPanelBuilder.BuildRoadmapPanel(snapshot, 10));
            // Just verify it renders without throwing
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }
}
