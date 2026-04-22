using DevTeam.Cli.Shell;

namespace DevTeam.ShellTests.Tests;

internal static class TerminalMouseScrollTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("TryParseWheelDelta_WheelUp_ReturnsPositiveDelta", TryParseWheelDelta_WheelUp_ReturnsPositiveDelta),
        new("TryParseWheelDelta_WheelDown_ReturnsNegativeDelta", TryParseWheelDelta_WheelDown_ReturnsNegativeDelta),
        new("TryParseWheelDelta_ModifiedWheelSequence_ReturnsExpectedDelta", TryParseWheelDelta_ModifiedWheelSequence_ReturnsExpectedDelta),
        new("TryParseWheelDelta_NonWheelSequence_ReturnsFalse", TryParseWheelDelta_NonWheelSequence_ReturnsFalse),
        new("TryGetWheelDelta_EscapeThenNormalKey_PreservesBufferedKey", TryGetWheelDelta_EscapeThenNormalKey_PreservesBufferedKey),
        new("TryGetWheelDelta_PartialMouseSequence_PreservesBufferedKeys", TryGetWheelDelta_PartialMouseSequence_PreservesBufferedKeys),
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

    private static Task TryParseWheelDelta_ModifiedWheelSequence_ReturnsExpectedDelta()
    {
        var success = TerminalMouseScroll.TryParseWheelDelta("\x1b[<68;52;12M", out var delta);

        Assert.That(success, "Expected modified wheel-up escape sequence to parse successfully.");
        Assert.That(delta == 1, $"Expected modified wheel-up delta 1 but got {delta}.");
        return Task.CompletedTask;
    }

    private static Task TryGetWheelDelta_EscapeThenNormalKey_PreservesBufferedKey()
    {
        TerminalMouseScroll.ClearPendingKeysForTests();
        var readCount = 0;
        var buffered = new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false);

        var success = TerminalMouseScroll.TryGetWheelDelta(
            new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, shift: false, alt: false, control: false),
            () => readCount == 0,
            () =>
            {
                readCount++;
                return buffered;
            },
            out var delta);

        Assert.That(!success, "Expected non-wheel Escape + key to be ignored for wheel scrolling.");
        Assert.That(delta == 0, $"Expected ignored sequence to leave delta at 0 but got {delta}.");
        Assert.That(TerminalMouseScroll.TryReadInputKey(() => false, () => throw new InvalidOperationException("Should use pending key first."), out var pendingKey), "Expected buffered key to remain available.");
        Assert.That(pendingKey.Key == ConsoleKey.A, $"Expected buffered key A but got {pendingKey.Key}.");
        Assert.That(!TerminalMouseScroll.TryReadInputKey(() => false, () => throw new InvalidOperationException("No keys should remain."), out _), "Expected buffered key queue to be drained.");
        TerminalMouseScroll.ClearPendingKeysForTests();
        return Task.CompletedTask;
    }

    private static Task TryGetWheelDelta_PartialMouseSequence_PreservesBufferedKeys()
    {
        TerminalMouseScroll.ClearPendingKeysForTests();
        var sequence = new Queue<ConsoleKeyInfo>(
        [
            new ConsoleKeyInfo('[', ConsoleKey.Oem4, shift: false, alt: false, control: false),
            new ConsoleKeyInfo('<', ConsoleKey.OemComma, shift: false, alt: false, control: false),
            new ConsoleKeyInfo('6', ConsoleKey.D6, shift: false, alt: false, control: false),
            new ConsoleKeyInfo('4', ConsoleKey.D4, shift: false, alt: false, control: false),
            new ConsoleKeyInfo(';', ConsoleKey.Oem1, shift: false, alt: false, control: false),
        ]);

        var success = TerminalMouseScroll.TryGetWheelDelta(
            new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, shift: false, alt: false, control: false),
            () => sequence.Count > 0,
            () => sequence.Dequeue(),
            out var delta);

        Assert.That(!success, "Expected partial escape sequence without terminator to be ignored for wheel scrolling.");
        Assert.That(delta == 0, $"Expected ignored partial sequence to leave delta at 0 but got {delta}.");

        var pending = new List<char>();
        while (TerminalMouseScroll.TryReadInputKey(() => false, () => throw new InvalidOperationException("No direct reads expected."), out var key))
        {
            pending.Add(key.KeyChar);
        }

        var pendingText = new string([.. pending]);
        Assert.That(string.Equals(pendingText, "[<64;", StringComparison.Ordinal), $"Expected buffered pending text '[<64;' but got '{pendingText}'.");
        TerminalMouseScroll.ClearPendingKeysForTests();
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
