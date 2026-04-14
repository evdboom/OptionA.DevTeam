using System.Text;
using DevTeam.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Hosts the interactive shell using Spectre.Console LiveDisplay with a
/// Layout-based dashboard for fixed-region rendering.
/// </summary>
internal static class SpectreShellHost
{
    private const int MaxRoadmapLines = 10;
    private const int RefreshMs = 100;
    private const int HeaderSize = 4;
    private const int InputSize = 6; // 4 content lines max (border top + 4 + border bottom)
    private const int AgentsSize = 8;
    private const int LeftColumnWidth = 60;
    // Panel border + padding consumes 4 chars on each side ("│ " left, " │" right).
    private const int LeftColumnContentWidth = LeftColumnWidth - 4;
    // Roadmap line: "○ <title> (<role>)" — 5 fixed chars overhead (check+space, space+parens).
    private const int RoadmapRoleMax = 12;
    private const int RoadmapTitleMax = LeftColumnContentWidth - 5 - RoadmapRoleMax;
    // Agent slot: "⚡ <role> #<id> <title>" — ~8 fixed chars overhead (icon, spaces, #, id≤3 digits).
    private const int AgentRoleMax = 14;
    private const int AgentTitleMax = LeftColumnContentWidth - 8 - AgentRoleMax;
    private const int ProgressPageStep = 10;    // messages per PageUp/PageDown
    private const int FallbackTerminalHeight = 40; // used when stdout is redirected

    internal static async Task RunAsync(ShellService shell, CancellationToken cancellationToken)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.Yes,
        });

        var inputBuffer = new StringBuilder();
        var historyCursor = -1; // -1 = not navigating history
        var savedDraft = string.Empty; // preserves unsent input while browsing history
        var scrollOffset = 0;  // 0 = auto-follow latest; N = scrolled N messages up

        await shell.InitializeAsync();

        // Build the Layout tree ONCE. Reuse the same instance and only
        // call .Update() on the leaf nodes each tick. This guarantees
        // the tree shape and rendered height never change between frames,
        // which is the prerequisite for Live display to overwrite correctly.
        var layout = BuildLayoutTree();
        UpdateLayout(layout, shell, string.Empty, scrollOffset);

        await console.Live(layout)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async context =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ReadInput(inputBuffer, shell, ref historyCursor, ref savedDraft, ref scrollOffset);
                    UpdateLayout(layout, shell, inputBuffer.ToString(), scrollOffset);
                    context.UpdateTarget(layout);

                    try { await Task.Delay(RefreshMs, cancellationToken); }
                    catch (OperationCanceledException) { break; }
                }
            });
    }

    /// <summary>
    /// Creates the Layout tree once. The same instance is reused every frame.
    /// Only leaf .Update() calls change — never the tree structure.
    /// </summary>
    private static Layout BuildLayoutTree()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(HeaderSize),
                new Layout("Body"),
                new Layout("Input").Size(InputSize));

        root["Body"].SplitColumns(
            new Layout("Left").Size(LeftColumnWidth),
            new Layout("Right"));

        root["Left"].SplitRows(
            new Layout("Agents").Size(AgentsSize),
            new Layout("Roadmap"));

        return root;
    }

    /// <summary>Updates every leaf panel in the pre-built layout tree.</summary>
    private static void UpdateLayout(Layout root, ShellService shell, string activeInput, int scrollOffset)
    {
        var snapshot = shell.LayoutSnapshot;
        var messages = shell.Messages;

        root["Header"].Update(BuildHeader(snapshot.Phase, shell.IsLoopRunning));
        root["Input"].Update(BuildInput(shell.PromptText, activeInput));

        if (snapshot.ShowMiddleRow)
        {
            root["Agents"].Update(BuildAgentsPanel(snapshot));

            // Compute how many roadmap lines can fit in the remaining left-column height
            // without overflowing: termHeight - header - input - agents slot - panel borders.
            var th = Console.IsOutputRedirected ? FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
            var roadmapBudget = Math.Max(2, th - HeaderSize - InputSize - AgentsSize - 3);
            root["Roadmap"].Update(BuildRoadmapPanel(snapshot, Math.Min(MaxRoadmapLines, roadmapBudget)));
        }
        else
        {
            root["Agents"].Update(BuildEmptyPanel("Agents"));
            root["Roadmap"].Update(BuildEmptyPanel("Roadmap"));
        }

        root["Right"].Update(BuildProgressPanel(messages, scrollOffset));
    }

    // ── Input handling ─────────────────────────────────────────────────────────

    private static void ReadInput(StringBuilder inputBuffer, ShellService shell, ref int historyCursor, ref string savedDraft, ref int scrollOffset)
    {
        if (Console.IsInputRedirected) return;

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);

            // PageUp → scroll progress pane up (older messages)
            if (key.Key == ConsoleKey.PageUp)
            {
                var total = shell.Messages.Count;
                scrollOffset = Math.Min(scrollOffset + ProgressPageStep, Math.Max(0, total - 1));
                continue;
            }

            // PageDown → scroll progress pane down (newer messages)
            if (key.Key == ConsoleKey.PageDown)
            {
                scrollOffset = Math.Max(0, scrollOffset - ProgressPageStep);
                continue;
            }

            // End → jump back to auto-follow latest
            if (key.Key == ConsoleKey.End)
            {
                scrollOffset = 0;
                continue;
            }

            // Home → jump to oldest visible messages
            if (key.Key == ConsoleKey.Home)
            {
                var total = shell.Messages.Count;
                scrollOffset = Math.Max(0, total - 1);
                continue;
            }

            // Shift+Enter or Ctrl+J → insert newline into the buffer
            // (Ctrl+J is more reliable across terminal emulators than Shift+Enter)
            if ((key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                || (key.Key == ConsoleKey.J && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                inputBuffer.Append('\n');
                historyCursor = -1;
                continue;
            }

            // Enter → submit
            if (key.Key == ConsoleKey.Enter)
            {
                var command = inputBuffer.ToString().Trim();
                inputBuffer.Clear();
                historyCursor = -1;
                savedDraft = string.Empty;
                if (!string.IsNullOrWhiteSpace(command))
                    _ = Task.Run(() => shell.ProcessInputAsync(command));
                continue;
            }

            // Up arrow → older history entry
            if (key.Key == ConsoleKey.UpArrow)
            {
                var history = shell.CommandHistory;
                if (history.Count == 0) continue;
                if (historyCursor == -1)
                {
                    savedDraft = inputBuffer.ToString();
                    historyCursor = history.Count - 1;
                }
                else if (historyCursor > 0)
                {
                    historyCursor--;
                }
                inputBuffer.Clear();
                inputBuffer.Append(history[historyCursor]);
                continue;
            }

            // Down arrow → newer history entry, or restore draft
            if (key.Key == ConsoleKey.DownArrow)
            {
                if (historyCursor == -1) continue;
                var history = shell.CommandHistory;
                if (historyCursor < history.Count - 1)
                {
                    historyCursor++;
                    inputBuffer.Clear();
                    inputBuffer.Append(history[historyCursor]);
                }
                else
                {
                    historyCursor = -1;
                    inputBuffer.Clear();
                    inputBuffer.Append(savedDraft);
                    savedDraft = string.Empty;
                }
                continue;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (inputBuffer.Length > 0) inputBuffer.Length--;
                historyCursor = -1;
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                inputBuffer.Clear();
                historyCursor = -1;
                savedDraft = string.Empty;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                inputBuffer.Append(key.KeyChar);
                historyCursor = -1;
            }
        }
    }

    // ── Panel builders ─────────────────────────────────────────────────────────

    private static Panel BuildHeader(WorkflowPhase phase, bool isRunning)
    {
        var phaseTag = phase switch
        {
            WorkflowPhase.Planning => "[yellow]Planning[/]",
            WorkflowPhase.ArchitectPlanning => "[cyan]Architect Planning[/]",
            WorkflowPhase.Execution => "[green]Execution[/]",
            _ => phase.ToString(),
        };
        var runTag = isRunning ? "  [dim]⏳ running[/]" : "";
        return new Panel(new Markup($"[teal]Phase[/]: [bold]{phaseTag}[/]{runTag}"))
            .Header("- [teal bold]DevTeam[/] -", Justify.Center)
            .BorderColor(Color.Purple3)
            .Expand();
    }

    private static Panel BuildInput(string promptText, string activeInput)
    {
        var label = promptText.TrimEnd().TrimEnd('>').TrimEnd();
        var lines = activeInput.Split('\n');
        const int MaxVisible = 4;

        string displayMarkup;
        if (lines.Length <= MaxVisible)
        {
            var parts = lines.Select((line, i) =>
                i == 0 ? $"[bold aqua]>[/] {Markup.Escape(line)}"
                       : $"  {Markup.Escape(line)}");
            displayMarkup = string.Join("\n", parts);
        }
        else
        {
            var overflow = lines.Length - MaxVisible;
            var visible = lines[^MaxVisible..];
            var parts = visible.Select((line, i) =>
                i == 0 ? $"[bold aqua]>[/] {Markup.Escape(line)}"
                       : $"  {Markup.Escape(line)}");
            displayMarkup = $"[dim](+{overflow} line(s) above)[/]\n" + string.Join("\n", parts);
        }

        return new Panel(new Markup(displayMarkup))
            .Header($"- [teal bold]{Markup.Escape(label)}[/] -")
            .BorderColor(Color.Purple3)
            .Expand();
    }

    private static Panel BuildEmptyPanel(string title) =>
        new Panel(new Markup("[dim]—[/]"))
            .Header($"- [teal bold]{title}[/] -")
            .BorderColor(Color.Grey)
            .Expand();

    private static Panel BuildAgentsPanel(ShellLayoutSnapshot snapshot)
    {
        var markup = snapshot.Agents.Count > 0
            ? string.Join("\n", snapshot.Agents.Select(FormatAgentSlot))
            : "[dim]No active agents[/]";
        return new Panel(new Markup(markup))
            .Header("- [teal bold]Agents[/] -")
            .BorderColor(Color.Purple3)
            .Expand();
    }

    private static Panel BuildRoadmapPanel(ShellLayoutSnapshot snapshot, int maxLines)
    {
        var items = snapshot.Roadmap.Take(maxLines).Select(FormatRoadmapSlot).ToList();
        if (snapshot.Roadmap.Count > maxLines)
            items.Add($"[dim]… {snapshot.Roadmap.Count - maxLines} more[/]");
        var markup = items.Count > 0 ? string.Join("\n", items) : "[dim]No issues[/]";
        return new Panel(new Markup(markup))
            .Header("- [teal bold]Roadmap[/] -")
            .BorderColor(Color.Purple3)
            .Expand();
    }

    private static Panel BuildProgressPanel(IReadOnlyList<ShellMessage> messages, int scrollOffset)
    {
        var termHeight = Console.IsOutputRedirected ? FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
        var termWidth = Console.IsOutputRedirected ? 120 : Math.Max(40, Console.WindowWidth);

        // Height budget: total rows minus the fixed layout slots and progress panel borders.
        var budget = Math.Max(4, termHeight - HeaderSize - InputSize - 3);

        // Progress panel inner content width: terminal width minus left column minus panel padding.
        var progressWidth = Math.Max(20, termWidth - LeftColumnWidth - 4);

        IRenderable content;
        if (messages.Count == 0)
        {
            content = new Markup("[grey]No events yet[/]");
        }
        else
        {
            // scrollOffset=0 → auto-follow latest. N → scrolled N items toward older.
            var end = Math.Clamp(messages.Count - scrollOffset, 1, messages.Count);
            var newerCount = messages.Count - end;

            var rendered = new List<IRenderable>();
            var remaining = budget;

            // Newer indicator at TOP — always visible even when the bottom overflows.
            if (newerCount > 0)
            {
                rendered.Add(new Markup($"[dim]↑ {newerCount} newer  ·  PgDn or End to follow[/]"));
                remaining--;
            }

            // Walk newest → oldest, stopping before we’d overflow the budget.
            var oldestShown = end;
            for (var i = end - 1; i >= 0 && remaining > 1; i--)
            {
                var h = EstimateMessageHeight(messages[i], progressWidth);
                if (h > remaining - 1) break; // keep 1 row for the older banner
                remaining -= h;
                rendered.Add(RenderMessage(messages[i]));
                oldestShown = i;
            }

            var olderCount = oldestShown;
            if (olderCount > 0)
                rendered.Add(new Markup($"[dim]↓ {olderCount} older  ·  PgUp for more[/]"));

            content = new Rows(rendered);
        }

        var scrolled = scrollOffset > 0;
        var header = scrolled
            ? "- [teal bold]Progress[/] [dim](scrolled · End to follow)[/] -"
            : "- [teal bold]Progress[/] -";
        return new Panel(content)
            .Header(header)
            .BorderColor(scrolled ? Color.Grey : Color.Purple3)
            .Expand();
    }

    /// <summary>
    /// Estimates the rendered row count for a message, accounting for line wrapping
    /// at the given panel content width.
    /// </summary>
    private static int EstimateMessageHeight(ShellMessage msg, int contentWidth)
    {
        if (msg.Kind == ShellMessageKind.Line)
        {
            var len = VisibleLength(msg.Markup);
            return Math.Max(1, (len + contentWidth - 1) / contentWidth);
        }
        // Nested panel: 2 border rows, each content line wraps within (contentWidth - 4).
        var innerWidth = Math.Max(1, contentWidth - 4);
        var total = 2;
        foreach (var line in msg.Markup.Split('\n'))
        {
            var len = VisibleLength(line);
            total += Math.Max(1, (len + innerWidth - 1) / innerWidth);
        }
        return total;
    }

    /// <summary>
    /// Counts visible characters in a Spectre markup string by skipping tag sequences.
    /// Handles Spectre escapes: [[ → one '[', ]] → one ']'.
    /// </summary>
    private static int VisibleLength(string markup)
    {
        var inTag = false;
        var count = 0;
        for (var i = 0; i < markup.Length; i++)
        {
            var c = markup[i];
            if (c == '[')
            {
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    count++;
                    i++;
                    continue;
                }
                inTag = true;
                continue;
            }
            if (c == ']')
            {
                if (inTag)
                {
                    inTag = false;
                    if (i + 1 < markup.Length && markup[i + 1] == ']')
                    {
                        count++;
                        i++;
                    }
                }
                continue;
            }
            if (!inTag) count++;
        }
        return count;
    }

    private static IRenderable RenderMessage(ShellMessage msg)
    {
        if (msg.Kind == ShellMessageKind.Panel)
        {
            var panel = new Panel(new Markup(msg.Markup));
            if (msg.Title is not null)
                panel.Header = msg.TitleJustify.HasValue
                    ? new PanelHeader(msg.Title, msg.TitleJustify.Value)
                    : new PanelHeader(msg.Title);
            if (msg.BorderColor is not null) panel.BorderStyle = new Style(foreground: msg.BorderColor.Value);
            return panel;
        }
        return new Markup(msg.Markup);
    }

    // ── Formatting helpers ─────────────────────────────────────────────────────

    private static string FormatAgentSlot(AgentSlot slot)
    {
        var icon = slot.Status == AgentRunStatus.Running ? "⚡" : "⏳";
        var role = slot.RoleSlug.Length > AgentRoleMax ? slot.RoleSlug[..(AgentRoleMax - 1)] + "…" : slot.RoleSlug;
        var title = slot.Title.Length > AgentTitleMax ? slot.Title[..(AgentTitleMax - 1)] + "…" : slot.Title;
        return $"[dim]{icon}[/] [cyan]{Markup.Escape(role)}[/] [dim]#{slot.IssueId}[/] {Markup.Escape(title)}";
    }

    private static string FormatRoadmapSlot(RoadmapSlot slot)
    {
        var (check, statusColor) = slot.Status switch
        {
            ItemStatus.Done => ("[green]✓[/]", "green"),
            ItemStatus.InProgress => ("[yellow]⚡[/]", "yellow"),
            ItemStatus.Blocked => ("[red]✗[/]", "red"),
            _ => ("[dim]○[/]", "dim"),
        };
        var role = slot.RoleSlug.Length > RoadmapRoleMax ? slot.RoleSlug[..(RoadmapRoleMax - 1)] + "…" : slot.RoleSlug;
        var title = slot.Title.Length > RoadmapTitleMax ? slot.Title[..(RoadmapTitleMax - 1)] + "…" : slot.Title;
        return $"{check} [{statusColor}]{Markup.Escape(title)}[/] [dim]({Markup.Escape(role)})[/]";
    }
}
