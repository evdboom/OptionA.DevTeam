using System.Text;
using DevTeam.Core;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Runs the shell in non-interactive (no-TTY) mode: reads commands from stdin line-by-line,
/// writes plain-text output to stdout. Used when --no-tty is set or the terminal is non-interactive.
/// </summary>
internal static class NonInteractiveShellHost
{
    internal static async Task RunAsync(ShellService shell, CancellationToken cancellationToken)
    {
        await shell.InitializeAsync();

        var messageIndex = 0;
        DrainMessages(shell, ref messageIndex);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break; // stdin closed
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await shell.ProcessInputAsync(line);
            DrainMessages(shell, ref messageIndex);
        }
    }

    private static void DrainMessages(ShellService shell, ref int index)
    {
        var messages = shell.Messages;
        for (; index < messages.Count; index++)
        {
            var msg = messages[index];
            var text = StripMarkup(msg.Markup);
            if (msg.Title is not null)
            {
                Console.WriteLine($"[{StripMarkup(msg.Title)}] {text}");
            }
            else
            {
                Console.WriteLine(text);
            }
        }
    }

    /// <summary>
    /// Strips Spectre.Console markup tags from a string, leaving only visible text.
    /// Handles escaped brackets: [[ → [ and ]] → ]
    /// </summary>
    internal static string StripMarkup(string markup)
    {
        var sb = new StringBuilder(markup.Length);
        var inTag = false;
        for (var i = 0; i < markup.Length; i++)
        {
            var c = markup[i];
            var next = i + 1 < markup.Length ? markup[i + 1] : '\0';

            if (c == '[' && next == '[')
            {
                sb.Append('[');
                i++;
            }
            else if (c == '[')
            {
                inTag = true;
            }
            else if (c == ']' && inTag)
            {
                inTag = false;
                if (next == ']') i++; // consume second ] of escaped ]]
            }
            else if (c == ']' && next == ']')
            {
                sb.Append(']');
                i++;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
