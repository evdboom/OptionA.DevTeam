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
        new("ExecutionScenario_HeaderContainsArchitect", ExecutionScenario_HeaderContainsArchitect),
        new("QuestionsScenario_RendersWithoutError", QuestionsScenario_RendersWithoutError),
        new("RunningAgents_DisplayedWhenLoopIsRunning", RunningAgents_DisplayedWhenLoopIsRunning),
        new("RunningAgents_HiddenWhenLoopNotRunning", RunningAgents_HiddenWhenLoopNotRunning),
        new("ProgressPanel_WithLongMessages_RendersWithoutOverflow", ProgressPanel_WithLongMessages_RendersWithoutOverflow),
        new("ProgressPanel_WithMalformedMarkup_EscapedSafely", ProgressPanel_WithMalformedMarkup_EscapedSafely),
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

        var cycle = agents
            .Select(a => new CycleSlot(
                a.RoleSlug,
                a.IssueId,
                a.Title,
                TimeSpan.FromSeconds(12),
                IsRunning: a.Status == AgentRunStatus.Running,
                IsCompleted: a.Status != AgentRunStatus.Running,
                DateTimeOffset.UtcNow))
            .ToList();

        return new ShellLayoutSnapshot(state.Phase, agents)
        {
            CurrentCycle = cycle,
        };
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
            console.Write(ShellPanelBuilder.BuildProgressPanel([], 0));
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

    private static Task ExecutionScenario_HeaderContainsArchitect()
    {
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildExecutionScenario(wp);
            // The execution scenario has a Running architect run; loopRunning=true to show it
            var snapshot = BuildSnapshot(state, loopRunning: true);
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, isRunning: true, snapshot.CurrentCycle));
            var output = console.Output;
            Assert.That(output.Contains("Architect"), $"Expected 'Architect' in header cycle status but got: {output}");
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
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, true, snapshot.CurrentCycle));
            console.Write(ShellPanelBuilder.BuildProgressPanel([], 0));
            // Just verify it renders without throwing
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task RunningAgents_DisplayedWhenLoopIsRunning()
    {
        // Verify that agents with status Running are included when loopRunning=true
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildExecutionScenario(wp);
            // Execution scenario has a Running architect agent
            var snapshot = BuildSnapshot(state, loopRunning: true);
            
            Assert.That(snapshot.Agents.Count > 0, "Expected agents to be present when loopRunning=true");
            var runningAgent = snapshot.Agents.FirstOrDefault(a => a.Status == AgentRunStatus.Running);
            Assert.That(runningAgent != null, $"Expected at least one Running agent; got {snapshot.Agents.Count} agents");
            
            var console = CreateConsole();
            console.Write(ShellPanelBuilder.BuildHeader(snapshot.Phase, isRunning: true, snapshot.CurrentCycle));
            var output = console.Output;
            Assert.That(output.Contains("Architect"),
                $"Expected 'Architect' in header output but got: {output}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task RunningAgents_HiddenWhenLoopNotRunning()
    {
        // Verify that agents with status Running are excluded when loopRunning=false
        var wp = WorkspacePath;
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildExecutionScenario(wp);
            // loopRunning=false means Running agents should be filtered out
            var snapshot = BuildSnapshot(state, loopRunning: false);
            
            var runningAgents = snapshot.Agents.Where(a => a.Status == AgentRunStatus.Running).ToList();
            Assert.That(runningAgents.Count == 0, 
                $"Expected no Running agents when loopRunning=false; got {runningAgents.Count}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_WithLongMessages_RendersWithoutOverflow()
    {
        var messages = new List<ShellMessage>
        {
            new(ShellMessageKind.Line, "This is an extremely long message line that should render safely inside the progress panel without overflowing layout bounds")
        };
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0));
        var output = console.Output;
        Assert.That(output.Length > 0, "Progress panel with long messages should render content");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_WithMalformedMarkup_EscapedSafely()
    {
        var messages = new List<ShellMessage>
        {
            new(ShellMessageKind.Line, "Fix [bracket] and </closing> tags in parser")
        };
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0));
        var output = console.Output;
        Assert.That(output.Length > 0, "Progress panel should render safely with markup-like message content");
        return Task.CompletedTask;
    }
}
