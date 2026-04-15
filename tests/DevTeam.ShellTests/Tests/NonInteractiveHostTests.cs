using DevTeam.Cli;
using DevTeam.Cli.Shell;

namespace DevTeam.ShellTests.Tests;

internal static class NonInteractiveHostTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("StripMarkup_RemovesColorTags", StripMarkup_RemovesColorTags),
        new("StripMarkup_HandlesEscapedBrackets", StripMarkup_HandlesEscapedBrackets),
        new("StripMarkup_PreservesPlainText", StripMarkup_PreservesPlainText),
        new("StripMarkup_EmptyString_ReturnsEmpty", StripMarkup_EmptyString_ReturnsEmpty),
        new("DetectLevel_RedMarkup_ReturnsError", DetectLevel_RedMarkup_ReturnsError),
        new("DetectLevel_YellowMarkup_ReturnsWarn", DetectLevel_YellowMarkup_ReturnsWarn),
        new("DetectLevel_GreenMarkup_ReturnsSuccess", DetectLevel_GreenMarkup_ReturnsSuccess),
        new("DetectLevel_DimMarkup_ReturnsDebug", DetectLevel_DimMarkup_ReturnsDebug),
        new("DetectLevel_PlainMarkup_ReturnsInfo", DetectLevel_PlainMarkup_ReturnsInfo),
        new("TtyDetection_NoTtyFlag_SetsNonInteractive", TtyDetection_NoTtyFlag_SetsNonInteractive),
    ];

    private static Task StripMarkup_RemovesColorTags()
    {
        var result = NonInteractiveShellHost.StripMarkup("[bold green]✓[/] [green]Success message[/]");
        Assert.That(!result.Contains("["), $"Expected no markup tags, got: {result}");
        Assert.Contains("✓", result);
        Assert.Contains("Success message", result);
        return Task.CompletedTask;
    }

    private static Task StripMarkup_HandlesEscapedBrackets()
    {
        var result = NonInteractiveShellHost.StripMarkup("Use [[/help]] to get help");
        Assert.Contains("[/help]", result);
        return Task.CompletedTask;
    }

    private static Task StripMarkup_PreservesPlainText()
    {
        var result = NonInteractiveShellHost.StripMarkup("No markup here at all.");
        Assert.That(result == "No markup here at all.", $"Expected plain text unchanged, got: {result}");
        return Task.CompletedTask;
    }

    private static Task StripMarkup_EmptyString_ReturnsEmpty()
    {
        var result = NonInteractiveShellHost.StripMarkup("");
        Assert.That(result == "", $"Expected empty string, got: '{result}'");
        return Task.CompletedTask;
    }

    private static Task DetectLevel_RedMarkup_ReturnsError()
    {
        var level = NonInteractiveShellHost.DetectLevel("[bold red]✗[/] [red]Something failed[/]");
        Assert.That(level == "error", $"Expected 'error', got '{level}'");
        return Task.CompletedTask;
    }

    private static Task DetectLevel_YellowMarkup_ReturnsWarn()
    {
        var level = NonInteractiveShellHost.DetectLevel("[bold yellow]⚠[/] [yellow]Warning[/]");
        Assert.That(level == "warn", $"Expected 'warn', got '{level}'");
        return Task.CompletedTask;
    }

    private static Task DetectLevel_GreenMarkup_ReturnsSuccess()
    {
        var level = NonInteractiveShellHost.DetectLevel("[bold green]✓[/] All good");
        Assert.That(level == "success", $"Expected 'success', got '{level}'");
        return Task.CompletedTask;
    }

    private static Task DetectLevel_DimMarkup_ReturnsDebug()
    {
        var level = NonInteractiveShellHost.DetectLevel("[dim]Hint: try /help[/]");
        Assert.That(level == "debug", $"Expected 'debug', got '{level}'");
        return Task.CompletedTask;
    }

    private static Task DetectLevel_PlainMarkup_ReturnsInfo()
    {
        var level = NonInteractiveShellHost.DetectLevel("Plain message with no color");
        Assert.That(level == "info", $"Expected 'info', got '{level}'");
        return Task.CompletedTask;
    }

    private static Task TtyDetection_NoTtyFlag_SetsNonInteractive()
    {
        // Verify that the option key "no-tty" is recognized by the validator
        var options = new Dictionary<string, List<string>> { ["no-tty"] = ["true"] };
        var value = DevTeam.Cli.CliOptionParser.GetBoolOption(options, "no-tty", false);
        Assert.That(value, "Expected --no-tty to be parsed as true");
        return Task.CompletedTask;
    }
}
