using Spectre.Console;

namespace DevTeam.Cli;

/// <summary>
/// Chat-style console rendering built on Spectre.Console.
/// All methods are safe to call on any thread; output is serialised via the ANSI console lock.
/// Methods that accept "text" escape it internally; methods accepting "markup" trust the caller.
/// </summary>
internal static class ChatConsole
{
    // ── Banner ────────────────────────────────────────────────────────────────

    public static void WriteBanner(string workspacePath)
    {
        AnsiConsole.Write(new Rule("[bold cyan]DevTeam[/]") { Style = Style.Parse("cyan dim"), Justification = Justify.Left });
        AnsiConsole.MarkupLine(
            $"Workspace: [cyan]{Markup.Escape(workspacePath)}[/]  " +
            "[dim]· /help for commands · /exit to quit · Tab to autocomplete[/]");
    }

    public static void WriteNoWorkspace()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]No workspace found.[/] Use [cyan]/init[/] [dim]--goal \"<your goal>\"[/] to get started.");
    }

    // ── Panels ────────────────────────────────────────────────────────────────

    /// <summary>Render a message panel from the devteam system. <paramref name="markup"/> may contain Spectre markup.</summary>
    public static void WriteSystem(string markup, string? header = null)
    {
        var panel = new Panel(new Markup(markup))
        {
            Header = new PanelHeader($" {Markup.Escape(header ?? "devteam")} ", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("grey"),
            Padding = new Padding(1, 0),
        };
        AnsiConsole.Write(panel);
    }

    /// <summary>Render a plain-text agent response in a coloured panel.</summary>
    public static void WriteAgent(string roleSlug, string text, string? outcome = null)
    {
        var borderColor = outcome switch
        {
            "completed" => "green",
            "blocked" => "yellow",
            "failed" => "red",
            _ => "cyan",
        };
        var headerText = outcome is not null ? $"{roleSlug} — {outcome}" : roleSlug;
        var panel = new Panel(new Markup(Markup.Escape(text)))
        {
            Header = new PanelHeader($" [{borderColor}]{Markup.Escape(headerText)}[/] ", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(borderColor),
            Padding = new Padding(1, 0),
        };
        AnsiConsole.Write(panel);
    }

    /// <summary>Render a question from the team as a prominent yellow panel.</summary>
    public static void WriteQuestion(string questionText, int? questionId = null, bool isBlocking = true, int index = 1, int total = 1)
    {
        var counter = total > 1 ? $" [dim]({index}/{total})[/]" : "";
        var blocking = isBlocking ? " [dim]blocking[/]" : "";
        var panel = new Panel(new Markup($"[white]{Markup.Escape(questionText)}[/]"))
        {
            Header = new PanelHeader($" [bold yellow]question{counter}{blocking}[/] ", Justify.Left),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("yellow"),
            Padding = new Padding(1, 0),
        };
        AnsiConsole.Write(panel);
    }

    // ── Compact lines ──────────────────────────────────────────────────────────

    /// <summary>Write a compact icon + message line. <paramref name="markup"/> may contain Spectre markup.</summary>
    public static void WriteEvent(string icon, string markup, string style = "dim")
    {
        AnsiConsole.MarkupLine($"[{style}]{Markup.Escape(icon)} {markup}[/]");
    }

    public static void WriteSuccess(string text)
        => AnsiConsole.MarkupLine($"[bold green]✓[/] [green]{Markup.Escape(text)}[/]");

    public static void WriteWarning(string text)
        => AnsiConsole.MarkupLine($"[bold yellow]⚠[/] [yellow]{Markup.Escape(text)}[/]");

    public static void WriteError(string text)
        => AnsiConsole.MarkupLine($"[bold red]✗[/] [red]{Markup.Escape(text)}[/]");

    /// <summary>Write a dim contextual hint. <paramref name="markup"/> may contain Spectre markup.</summary>
    public static void WriteHint(string markup)
        => AnsiConsole.MarkupLine($"[dim]{markup}[/]");

    // ── Loop log formatting ───────────────────────────────────────────────────

    /// <summary>Format a raw log line emitted by the execution loop.</summary>
    public static void WriteLoopLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var trimmed = message.TrimStart();
        var pad = new string(' ', message.Length - trimmed.Length);
        var escaped = Markup.Escape(trimmed);

        if (trimmed.StartsWith("Iteration ", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"\n[grey]{pad}── {escaped} ──[/]");
        }
        else if (trimmed.StartsWith("Running issue #", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith("Bootstrapped:", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[dim]{pad}→ {escaped}[/]");
        }
        else if (trimmed.StartsWith("Still running", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[dim]{pad}⏳ {escaped}[/]");
        }
        else if (trimmed.StartsWith("Outcome:", StringComparison.OrdinalIgnoreCase))
        {
            var outcome = trimmed["Outcome:".Length..].Trim();
            var color = outcome switch { "completed" => "green", "blocked" => "yellow", "failed" => "red", _ => "grey" };
            AnsiConsole.MarkupLine($"[{color}]{pad}  ✓ outcome: {Markup.Escape(outcome)}[/]");
        }
        else if (trimmed.StartsWith("Budget", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[dim]{pad}💰 {escaped}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]{pad}{escaped}[/]");
        }
    }

    // ── Prompt ────────────────────────────────────────────────────────────────

    public static void WritePrompt(string text)
    {
        if (!Console.IsOutputRedirected)
            AnsiConsole.Markup($"[bold cyan]{Markup.Escape(text)}[/]");
        else
            Console.Write(text);
    }
}
