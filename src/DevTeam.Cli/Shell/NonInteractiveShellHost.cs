using System.Text;
using System.Text.Json;
using DevTeam.Core;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Runs the shell in non-interactive (no-TTY) mode: reads commands from stdin line-by-line,
/// writes plain-text output to stdout. Used when --no-tty is set or the terminal is non-interactive.
/// </summary>
internal static class NonInteractiveShellHost
{
    internal static async Task RunAsync(ShellService shell, CancellationToken cancellationToken, string outputFormat = "plain")
    {
        var useJsonl = string.Equals(outputFormat, "jsonl", StringComparison.OrdinalIgnoreCase);
        await shell.InitializeAsync();

        var messageIndex = 0;
        DrainMessages(shell, ref messageIndex, useJsonl);

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
            DrainMessages(shell, ref messageIndex, useJsonl);
        }
    }

    private static void DrainMessages(ShellService shell, ref int index, bool useJsonl)
    {
        var messages = shell.Messages;
        for (; index < messages.Count; index++)
        {
            var msg = messages[index];
            if (useJsonl)
            {
                var text = StripMarkup(msg.Markup);
                var level = DetectLevel(msg.Markup);
                var entry = new
                {
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    level,
                    panel = msg.Title is not null ? StripMarkup(msg.Title) : null,
                    message = text
                };
                Console.WriteLine(JsonSerializer.Serialize(entry));
            }
            else
            {
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
    }

    internal static string DetectLevel(string markup)
    {
        if (markup.Contains("[bold red]") || markup.Contains("[red]")) return "error";
        if (markup.Contains("[bold yellow]") || markup.Contains("[yellow]")) return "warn";
        if (markup.Contains("[bold green]") || markup.Contains("[green]")) return "success";
        if (markup.Contains("[dim]")) return "debug";
        return "info";
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
