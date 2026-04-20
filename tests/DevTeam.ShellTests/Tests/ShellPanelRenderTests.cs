using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;
using Spectre.Console.Testing;

namespace DevTeam.ShellTests.Tests;

internal static class ShellPanelRenderTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("HeaderPanel_PlanningPhase_ContainsPhaseLabel", HeaderPanel_PlanningPhase_ContainsPhaseLabel),
        new("HeaderPanel_ExecutionPhase_Running_ContainsRunningLabel", HeaderPanel_ExecutionPhase_Running_ContainsRunningLabel),
        new("AgentsPanel_NoAgents_ContainsNoActiveAgents", AgentsPanel_NoAgents_ContainsNoActiveAgents),
        new("AgentsPanel_WithRunningAgent_ContainsAgentInfo", AgentsPanel_WithRunningAgent_ContainsAgentInfo),
        new("RoadmapPanel_WithIssues_ContainsIssueInfo", RoadmapPanel_WithIssues_ContainsIssueInfo),
        new("RoadmapPanel_DoneIssue_ContainsDoneIndicator", RoadmapPanel_DoneIssue_ContainsDoneIndicator),
        new("EmptyPanel_ContainsTitle", EmptyPanel_ContainsTitle),
        new("VisibleLength_PlainText_ReturnsCorrectCount", VisibleLength_PlainText_ReturnsCorrectCount),
        new("VisibleLength_MarkupStripped_ReturnsVisibleOnly", VisibleLength_MarkupStripped_ReturnsVisibleOnly),
        new("VisibleLength_EscapedBracket_CountsAsOne", VisibleLength_EscapedBracket_CountsAsOne),
        new("StripMarkup_PlainText_Unchanged", StripMarkup_PlainText_Unchanged),
        new("StripMarkup_RemovesTags", StripMarkup_RemovesTags),
        new("StripMarkup_ClosingTag_Removed", StripMarkup_ClosingTag_Removed),
        new("StripMarkup_EscapedBrackets_Preserved", StripMarkup_EscapedBrackets_Preserved),
        new("ProgressPanel_MalformedMarkup_DoesNotThrow", ProgressPanel_MalformedMarkup_DoesNotThrow),
        new("PanelHeader_WithBrackets_EscapedSafely", PanelHeader_WithBrackets_EscapedSafely),
    ];

    private static TestConsole CreateConsole()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        return console;
    }

    private static Task HeaderPanel_PlanningPhase_ContainsPhaseLabel()
    {
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildHeader(WorkflowPhase.Planning, isRunning: false));
        var output = console.Output;
        Assert.That(output.Contains("Planning"), $"Expected 'Planning' in output but got: {output}");
        return Task.CompletedTask;
    }

    private static Task HeaderPanel_ExecutionPhase_Running_ContainsRunningLabel()
    {
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildHeader(WorkflowPhase.Execution, isRunning: true));
        var output = console.Output;
        Assert.That(output.Contains("Execution"), $"Expected 'Execution' in output but got: {output}");
        Assert.That(output.Contains("running"), $"Expected 'running' in output but got: {output}");
        return Task.CompletedTask;
    }

    private static Task AgentsPanel_NoAgents_ContainsNoActiveAgents()
    {
        var console = CreateConsole();
        var snapshot = new ShellLayoutSnapshot(WorkflowPhase.Execution, false, [], []);
        console.Write(ShellPanelBuilder.BuildAgentsPanel(snapshot));
        var output = console.Output;
        Assert.That(output.Contains("No active agents"), $"Expected 'No active agents' but got: {output}");
        return Task.CompletedTask;
    }

    private static Task AgentsPanel_WithRunningAgent_ContainsAgentInfo()
    {
        var console = CreateConsole();
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            ShowMiddleRow: true,
            Agents: [new AgentSlot(1, 5, "developer", "Build API", AgentRunStatus.Running)],
            Roadmap: []);
        console.Write(ShellPanelBuilder.BuildAgentsPanel(snapshot));
        var output = console.Output;
        Assert.That(output.Contains("developer"), $"Expected 'developer' in output but got: {output}");
        Assert.That(output.Contains("#5"), $"Expected '#5' in output but got: {output}");
        Assert.That(output.Contains("Build API"), $"Expected 'Build API' in output but got: {output}");
        return Task.CompletedTask;
    }

    private static Task RoadmapPanel_WithIssues_ContainsIssueInfo()
    {
        var console = CreateConsole();
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            ShowMiddleRow: true,
            Agents: [],
            Roadmap: [new RoadmapSlot(5, "Build API", "developer", ItemStatus.Open)]);
        console.Write(ShellPanelBuilder.BuildRoadmapPanel(snapshot, 10));
        var output = console.Output;
        Assert.That(output.Contains("Build API"), $"Expected 'Build API' in output but got: {output}");
        Assert.That(output.Contains("developer"), $"Expected 'developer' in output but got: {output}");
        return Task.CompletedTask;
    }

    private static Task RoadmapPanel_DoneIssue_ContainsDoneIndicator()
    {
        var console = CreateConsole();
        var snapshot = new ShellLayoutSnapshot(
            WorkflowPhase.Execution,
            ShowMiddleRow: true,
            Agents: [],
            Roadmap: [new RoadmapSlot(5, "Build API", "developer", ItemStatus.Done)]);
        console.Write(ShellPanelBuilder.BuildRoadmapPanel(snapshot, 10));
        var output = console.Output;
        Assert.That(output.Contains("✓") || output.Contains("done") || output.Contains("Done"),
            $"Expected done indicator in output but got: {output}");
        return Task.CompletedTask;
    }

    private static Task EmptyPanel_ContainsTitle()
    {
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildEmptyPanel("MyTitle"));
        var output = console.Output;
        Assert.That(output.Contains("MyTitle"), $"Expected 'MyTitle' in output but got: {output}");
        return Task.CompletedTask;
    }

    private static Task VisibleLength_PlainText_ReturnsCorrectCount()
    {
        var result = ShellPanelBuilder.VisibleLength("hello");
        Assert.That(result == 5, $"Expected 5 but got {result}");
        return Task.CompletedTask;
    }

    private static Task VisibleLength_MarkupStripped_ReturnsVisibleOnly()
    {
        var result = ShellPanelBuilder.VisibleLength("[bold red]hi[/]");
        Assert.That(result == 2, $"Expected 2 but got {result}");
        return Task.CompletedTask;
    }

    private static Task VisibleLength_EscapedBracket_CountsAsOne()
    {
        var result = ShellPanelBuilder.VisibleLength("[[");
        Assert.That(result == 1, $"Expected 1 but got {result}");
        return Task.CompletedTask;
    }

    private static Task StripMarkup_PlainText_Unchanged()
    {
        var result = NonInteractiveShellHost.StripMarkup("hello world");
        Assert.That(result == "hello world", $"Expected 'hello world' but got '{result}'");
        return Task.CompletedTask;
    }

    private static Task StripMarkup_RemovesTags()
    {
        var result = NonInteractiveShellHost.StripMarkup("[bold red]hello[/]");
        Assert.That(result == "hello", $"Expected 'hello' but got '{result}'");
        return Task.CompletedTask;
    }

    private static Task StripMarkup_ClosingTag_Removed()
    {
        var result = NonInteractiveShellHost.StripMarkup("[dim]foo[/] bar");
        Assert.That(result == "foo bar", $"Expected 'foo bar' but got '{result}'");
        return Task.CompletedTask;
    }

    private static Task StripMarkup_EscapedBrackets_Preserved()
    {
        var result = NonInteractiveShellHost.StripMarkup("[[bold]]");
        Assert.That(result == "[bold]", $"Expected '[bold]' but got '{result}'");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_MalformedMarkup_DoesNotThrow()
    {
        var console = CreateConsole();
        var messages = new List<ShellMessage>
        {
            new(ShellMessageKind.Line, "[dim]ok[/]"),
            new(ShellMessageKind.Line, "[/boom] this used to crash markup parsing")
        };

        console.Write(ShellPanelBuilder.BuildProgressPanel(messages, scrollOffset: 0, termHeightOverride: 40));
        var output = console.Output;

        Assert.That(output.Contains("this used to crash markup parsing", StringComparison.Ordinal),
            $"Expected malformed-markup message to render safely, but got: {output}");
        return Task.CompletedTask;
    }

    private static Task PanelHeader_WithBrackets_EscapedSafely()
    {
        // Verify that panel titles with unescaped brackets (from issue titles containing code patterns)
        // are properly escaped and don't crash the Spectre.Console Markup parser
        var console = CreateConsole();
        var message = new ShellMessage(
            ShellMessageKind.Panel,
            "Panel content here",
            Title: "architect — Implement [DllImport] rules (AOT040)");

        var renderable = ShellPanelBuilder.RenderMessage(message);
        console.Write(renderable);
        var output = console.Output;

        // Should render without throwing InvalidOperationException
        Assert.That(output.Length > 0, "Expected panel to render with bracketed title");
        return Task.CompletedTask;
    }
}
