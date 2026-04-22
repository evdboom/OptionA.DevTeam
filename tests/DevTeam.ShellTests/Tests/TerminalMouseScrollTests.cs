using DevTeam.Cli.Shell;

namespace DevTeam.ShellTests.Tests;

internal static class TerminalMouseScrollTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("TryParseWheelDelta_WheelUp_ReturnsPositiveDelta", TryParseWheelDelta_WheelUp_ReturnsPositiveDelta),
        new("TryParseWheelDelta_WheelDown_ReturnsNegativeDelta", TryParseWheelDelta_WheelDown_ReturnsNegativeDelta),
        new("TryParseWheelDelta_NonWheelSequence_ReturnsFalse", TryParseWheelDelta_NonWheelSequence_ReturnsFalse),
        new("ApplyWheelDelta_ClampsAtBounds", ApplyWheelDelta_ClampsAtBounds),
    ];

    private static Task TryParseWheelDelta_WheelUp_ReturnsPositiveDelta()
    {
        var success = TerminalMouseScroll.TryParseWheelDelta("\x1b[<64;52;12M", out var delta);

        Assert.That(success, "Expected wheel-up escape sequence to parse successfully.");
        Assert.That(delta == 1, $"Expected wheel-up delta 1 but got {delta}.");
        return Task.CompletedTask;
    }

    private static Task TryParseWheelDelta_WheelDown_ReturnsNegativeDelta()
    {
        var success = TerminalMouseScroll.TryParseWheelDelta("\x1b[<65;52;12M", out var delta);

        Assert.That(success, "Expected wheel-down escape sequence to parse successfully.");
        Assert.That(delta == -1, $"Expected wheel-down delta -1 but got {delta}.");
        return Task.CompletedTask;
    }

    private static Task TryParseWheelDelta_NonWheelSequence_ReturnsFalse()
    {
        var success = TerminalMouseScroll.TryParseWheelDelta("\x1b[<0;52;12M", out var delta);

        Assert.That(!success, "Expected non-wheel mouse sequence to be ignored.");
        Assert.That(delta == 0, $"Expected ignored sequence to leave delta at 0 but got {delta}.");
        return Task.CompletedTask;
    }

    private static Task ApplyWheelDelta_ClampsAtBounds()
    {
        var belowZero = TerminalMouseScroll.ApplyWheelDelta(1, -10, 20);
        var aboveMax = TerminalMouseScroll.ApplyWheelDelta(18, 10, 20);

        Assert.That(belowZero == 0, $"Expected clamp to 0 but got {belowZero}.");
        Assert.That(aboveMax == 20, $"Expected clamp to max offset 20 but got {aboveMax}.");
        return Task.CompletedTask;
    }
}