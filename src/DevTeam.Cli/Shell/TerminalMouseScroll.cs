using System.Globalization;
using System.Text;

namespace DevTeam.Cli.Shell;

internal static class TerminalMouseScroll
{
    private const string EnableMouseTrackingSequence = "\x1b[?1000h\x1b[?1006h";
    private const string DisableMouseTrackingSequence = "\x1b[?1000l\x1b[?1006l";
    private const int WheelUpButtonCode = 64;
    private const int WheelDownButtonCode = 65;
    private const int ModifierBitsMask = 4 | 8 | 16;
    private const int MaxEscapeSequenceLength = 32;
    private const int EscapeSequenceProbeMs = 6;
    private static readonly Queue<ConsoleKeyInfo> PendingKeys = new();

    internal static void EnableTracking()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Write(EnableMouseTrackingSequence);
        }
    }

    internal static void DisableTracking()
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Write(DisableMouseTrackingSequence);
        }
    }

    internal static bool TryHandleWheel(ConsoleKeyInfo key, IReadOnlyList<ShellMessage> messages, ref int scrollOffset, int contentWidth)
    {
        if (!TryGetWheelDelta(key, () => Console.KeyAvailable, () => Console.ReadKey(intercept: true), out var delta))
        {
            return false;
        }

        var terminalHeight = Console.IsOutputRedirected
            ? ShellPanelBuilder.FallbackTerminalHeight
            : Math.Max(20, Console.WindowHeight);
        var maxScrollOffset = ShellPanelBuilder.MaxScrollOffset(messages, terminalHeight, contentWidth);
        scrollOffset = ApplyWheelDelta(scrollOffset, delta * MouseWheelStep(), maxScrollOffset);
        return true;
    }

    internal static bool TryReadInputKey(Func<bool> isKeyAvailable, Func<ConsoleKeyInfo> readKey, out ConsoleKeyInfo key)
    {
        if (TryReadPendingKey(out key))
        {
            return true;
        }

        if (!isKeyAvailable())
        {
            key = default;
            return false;
        }

        key = readKey();
        return true;
    }

    internal static int ApplyWheelDelta(int scrollOffset, int delta, int maxScrollOffset) =>
        Math.Clamp(scrollOffset + delta, 0, Math.Max(0, maxScrollOffset));

    internal static int MouseWheelStep()
    {
        var terminalHeight = Console.IsOutputRedirected
            ? ShellPanelBuilder.FallbackTerminalHeight
            : Math.Max(20, Console.WindowHeight);
        return Math.Max(3, ShellPanelBuilder.ContentRowCount(terminalHeight) / 8);
    }

    internal static bool TryGetWheelDelta(ConsoleKeyInfo key, Func<bool> isKeyAvailable, Func<ConsoleKeyInfo> readKey, out int delta)
    {
        delta = 0;
        if (key.Key != ConsoleKey.Escape)
        {
            return false;
        }

        if (!isKeyAvailable() && !TryAwaitEscapeFollowUp(isKeyAvailable))
        {
            return false;
        }

        var sequence = ReadEscapeSequence(key, isKeyAvailable, readKey, out var consumedKeys);
        if (TryParseWheelDelta(sequence, out delta))
        {
            return true;
        }

        EnqueuePendingKeys(consumedKeys);
        return false;
    }

    internal static bool TryParseWheelDelta(string sequence, out int delta)
    {
        delta = 0;
        if (!sequence.StartsWith("\x1b[<", StringComparison.Ordinal)
            || sequence.Length < 7
            || sequence[^1] is not ('M' or 'm'))
        {
            return false;
        }

        var firstSeparator = sequence.IndexOf(';', 3);
        if (firstSeparator < 0)
        {
            return false;
        }

        if (sequence.IndexOf(';', firstSeparator + 1) < 0)
        {
            return false;
        }

        var buttonText = sequence[3..firstSeparator];
        if (!int.TryParse(buttonText, NumberStyles.None, CultureInfo.InvariantCulture, out var buttonCode))
        {
            return false;
        }

        var normalizedButtonCode = buttonCode & ~ModifierBitsMask;
        delta = normalizedButtonCode switch
        {
            WheelUpButtonCode => 1,
            WheelDownButtonCode => -1,
            _ => 0,
        };

        return delta != 0;
    }

    private static bool TryAwaitEscapeFollowUp(Func<bool> isKeyAvailable)
    {
        var spin = new SpinWait();
        var deadline = Environment.TickCount64 + EscapeSequenceProbeMs;

        while (Environment.TickCount64 <= deadline)
        {
            if (isKeyAvailable())
            {
                return true;
            }

            spin.SpinOnce();
        }

        return false;
    }

    internal static void ClearPendingKeysForTests()
    {
        PendingKeys.Clear();
    }

    private static bool TryReadPendingKey(out ConsoleKeyInfo key)
    {
        if (PendingKeys.Count > 0)
        {
            key = PendingKeys.Dequeue();
            return true;
        }

        key = default;
        return false;
    }

    private static string ReadEscapeSequence(ConsoleKeyInfo firstKey, Func<bool> isKeyAvailable, Func<ConsoleKeyInfo> readKey, out List<ConsoleKeyInfo> consumedKeys)
    {
        var builder = new StringBuilder();
        builder.Append(firstKey.KeyChar);
        consumedKeys = [];
        while (isKeyAvailable() && builder.Length < MaxEscapeSequenceLength)
        {
            var key = readKey();
            consumedKeys.Add(key);
            builder.Append(key.KeyChar);
            if (key.KeyChar is 'M' or 'm')
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static void EnqueuePendingKeys(IEnumerable<ConsoleKeyInfo> keys)
    {
        foreach (var key in keys)
        {
            PendingKeys.Enqueue(key);
        }
    }
}
