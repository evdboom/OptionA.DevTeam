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
        new("HeaderPanel_WithCycleStatus_ShowsRunningAndDone", HeaderPanel_WithCycleStatus_ShowsRunningAndDone),
        new("ProgressPanel_NoEvents_ContainsPlaceholder", ProgressPanel_NoEvents_ContainsPlaceholder),
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

    private static Task HeaderPanel_WithCycleStatus_ShowsRunningAndDone()
    {
        var console = CreateConsole();
        var now = DateTimeOffset.UtcNow;
        var cycle = new List<CycleSlot>
        {
            new("orchestrator", null, "Selecting next execution batch", TimeSpan.FromSeconds(74), IsRunning: true, IsCompleted: false, now),
            new("developer", 4, "Implement rules", TimeSpan.FromSeconds(45), IsRunning: true, IsCompleted: false, now),
            new("tester", 6, "Verify issue", TimeSpan.FromSeconds(65), IsRunning: false, IsCompleted: true, now),
        };

        console.Write(ShellPanelBuilder.BuildHeader(WorkflowPhase.Execution, isRunning: true, cycle));
        var output = console.Output;

        Assert.That(output.Contains("Execution"), $"Expected execution phase in header but got: {output}");
        Assert.That(output.Contains("Orchestrator"), $"Expected orchestrator line but got: {output}");
        Assert.That(output.Contains("issue #4"), $"Expected issue #4 line in header but got: {output}");
        Assert.That(output.Contains("done"), $"Expected completed marker but got: {output}");
        return Task.CompletedTask;
    }

    private static Task ProgressPanel_NoEvents_ContainsPlaceholder()
    {
        var console = CreateConsole();
        console.Write(ShellPanelBuilder.BuildProgressPanel([], 0));
        var output = console.Output;
        Assert.That(output.Contains("No events yet"), $"Expected 'No events yet' placeholder but got: {output}");
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
