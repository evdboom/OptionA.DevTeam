using System.Globalization;
using System.Text;

namespace DevTeam.Cli.Shell;

internal static class TerminalMouseScroll
{
    private const string EnableMouseTrackingSequence = "\x1b[?1000h\x1b[?1006h";
    private const string DisableMouseTrackingSequence = "\x1b[?1000l\x1b[?1006l";
    private const int WheelUpButtonCode = 64;
    private const int WheelDownButtonCode = 65;

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
        if (key.Key != ConsoleKey.Escape || !isKeyAvailable())
        {
            return false;
        }

        var sequence = ReadEscapeSequence(key, isKeyAvailable, readKey);
        return TryParseWheelDelta(sequence, out delta);
    }

    internal static bool TryParseWheelDelta(string sequence, out int delta)
    {
        delta = 0;
        if (!sequence.StartsWith("\x1b[<", StringComparison.Ordinal))
        {
            return false;
        }

        var firstSeparator = sequence.IndexOf(';', 3);
        if (firstSeparator < 0)
        {
            return false;
        }

        var buttonText = sequence[3..firstSeparator];
        if (!int.TryParse(buttonText, NumberStyles.None, CultureInfo.InvariantCulture, out var buttonCode))
        {
            return false;
        }

        delta = buttonCode switch
        {
            WheelUpButtonCode => 1,
            WheelDownButtonCode => -1,
            _ => 0,
        };

        return delta != 0;
    }

    private static string ReadEscapeSequence(ConsoleKeyInfo firstKey, Func<bool> isKeyAvailable, Func<ConsoleKeyInfo> readKey)
    {
        var builder = new StringBuilder();
        builder.Append(firstKey.KeyChar);
        while (isKeyAvailable())
        {
            builder.Append(readKey().KeyChar);
        }

        return builder.ToString();
    }
}
