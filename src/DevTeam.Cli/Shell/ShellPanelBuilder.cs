using DevTeam.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DevTeam.Cli.Shell;

/// <summary>
/// Stateless panel builder extracted from SpectreShellHost so tests can render
/// individual panels without standing up the full live display.
/// </summary>
internal static class ShellPanelBuilder
{
    internal const int LeftColumnWidth = 60;
    // Panel border + padding consumes 4 chars on each side ("│ " left, " │" right).
    internal const int LeftColumnContentWidth = LeftColumnWidth - 4;
    // Roadmap line: "○ <title> (<role>)" — 5 fixed chars overhead (check+space, space+parens).
    internal const int RoadmapRoleMax = 12;
    internal const int RoadmapTitleMax = LeftColumnContentWidth - 5 - RoadmapRoleMax;
    // Agent slot: "⚡ <role> #<id> <title>" — ~8 fixed chars overhead (icon, spaces, #, id≤3 digits).
    internal const int AgentRoleMax = 14;
    internal const int AgentTitleMax = LeftColumnContentWidth - 8 - AgentRoleMax;
    internal const int FallbackTerminalHeight = 40;

    private const int HeaderSize = 4;
    private const int InputSize = 6;

    internal static Panel BuildHeader(WorkflowPhase phase, bool isRunning)
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

    internal static Panel BuildInput(string promptText, string activeInput, int cursorPosition = -1)
    {
        var label = promptText.TrimEnd().TrimEnd('>').TrimEnd();

        // Insert the cursor marker (▌) at cursorPosition within the raw text.
        // cursorPosition == -1 means "no cursor" (e.g. non-interactive rendering).
        string displayText;
        if (cursorPosition >= 0)
        {
            var clamped = Math.Clamp(cursorPosition, 0, activeInput.Length);
            displayText = activeInput[..clamped] + "▌" + activeInput[clamped..];
        }
        else
        {
            displayText = activeInput;
        }

        var lines = displayText.Split('\n');
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

    internal static Panel BuildEmptyPanel(string title) =>
        new Panel(new Markup("[dim]—[/]"))
            .Header($"- [teal bold]{title}[/] -")
            .BorderColor(Color.Grey)
            .Expand();

    internal static Panel BuildAgentsPanel(ShellLayoutSnapshot snapshot)
    {
        var markup = snapshot.Agents.Count > 0
            ? string.Join("\n", snapshot.Agents.Select(FormatAgentSlot))
            : "[dim]No active agents[/]";
        return new Panel(new Markup(markup))
            .Header("- [teal bold]Agents[/] -")
            .BorderColor(Color.Purple3)
            .Expand();
    }

    internal static Panel BuildRoadmapPanel(ShellLayoutSnapshot snapshot, int maxLines)
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

    internal static Panel BuildProgressPanel(IReadOnlyList<ShellMessage> messages, int scrollOffset, int termHeightOverride = 0)
    {
        var termHeight = termHeightOverride > 0 ? termHeightOverride
            : Console.IsOutputRedirected ? FallbackTerminalHeight : Math.Max(20, Console.WindowHeight);
        var termWidth = Console.IsOutputRedirected ? 120 : Math.Max(40, Console.WindowWidth);

        var budget = ComputeLineBudget(termHeight);
        var progressWidth = Math.Max(20, termWidth - LeftColumnWidth - 4);

        IRenderable content;
        var allLines = FlattenMessages(messages);

        if (allLines.Length == 0)
        {
            content = CreateMarkupSafe("[grey]No events yet[/]");
        }
        else
        {
            var totalLines = allLines.Length;
            // Reserve 2 rows for the above/below hint lines so panel never overflows budget.
            var contentRows = Math.Max(1, budget - 2);

            // windowEnd: the exclusive upper bound of the visible window.
            // Lower bound is 1 (not contentRows) so scrolling can reach the very oldest line.
            var windowEnd = Math.Clamp(totalLines - scrollOffset, 1, totalLines);

            // Fill backwards from windowEnd, counting actual rendered rows per line
            // (accounts for word-wrap of wide lines so we never overflow the panel).
            var selectedLines = new List<string>();
            var usedRows = 0;
            var windowStart = windowEnd;

            for (var i = windowEnd - 1; i >= 0; i--)
            {
                var rowHeight = EstimateLineHeight(allLines[i], progressWidth);
                if (usedRows + rowHeight > contentRows)
                {
                    // If nothing fits yet (degenerate: one line is wider than the whole budget),
                    // force-include it rather than showing an empty panel.
                    if (selectedLines.Count == 0)
                    {
                        selectedLines.Add(allLines[i]);
                        windowStart = i;
                    }
                    break;
                }
                usedRows += rowHeight;
                windowStart = i;
                selectedLines.Add(allLines[i]);
            }

            selectedLines.Reverse();

            var linesAbove = windowStart;
            var linesBelow = totalLines - windowEnd;

            var rendered = new List<IRenderable>();
            if (linesAbove > 0)
                rendered.Add(CreateMarkupSafe($"[dim]▲ {linesAbove} lines above  ·  PgUp for more[/]"));
            foreach (var line in selectedLines)
                rendered.Add(CreateMarkupSafe(line));
            if (linesBelow > 0)
                rendered.Add(CreateMarkupSafe($"[dim]▼ {linesBelow} lines below  ·  PgDn or End to follow[/]"));

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
    /// Flattens all messages into a single array of markup strings — one entry per logical line.
    /// Panel messages prepend a dim header separator line. This enables line-accurate virtual
    /// scrolling without any message truncation.
    /// </summary>
    internal static string[] FlattenMessages(IReadOnlyList<ShellMessage> messages)
    {
        var lines = new List<string>();
        foreach (var msg in messages)
        {
            if (msg.Kind == ShellMessageKind.Panel && msg.Title is not null)
                lines.Add($"[dim]── {Markup.Escape(msg.Title)} ──[/]");
            foreach (var line in msg.Markup.Split('\n'))
                lines.Add(line.TrimEnd('\r')); // strip \r from Windows-style \r\n line endings
        }
        return lines.ToArray();
    }

    /// <summary>
    /// Returns the maximum useful scroll offset: the offset at which the oldest content
    /// fills the panel exactly (linesAbove = 0). Scrolling further would leave blank space.
    /// <para>
    /// When <paramref name="contentWidth"/> is provided (greater than 0) the offset is
    /// computed by simulating the same backward-fill used in
    /// <see cref="BuildProgressPanel"/>, accounting for lines that wrap across multiple
    /// terminal rows.  This is necessary when help or agent output contains long lines —
    /// without it, <c>MaxScrollOffset</c> is capped too low and the oldest lines can
    /// never be reached by scrolling.
    /// </para>
    /// <para>
    /// When <paramref name="contentWidth"/> is 0 (default) the legacy flat-line formula
    /// <c>flatCount − ContentRowCount</c> is used.  This is kept for callers and tests
    /// that don't have a content width readily available and work with short messages that
    /// are guaranteed not to wrap.
    /// </para>
    /// </summary>
    internal static int MaxScrollOffset(IReadOnlyList<ShellMessage> messages, int termHeight, int contentWidth = 0)
    {
        var allLines = FlattenMessages(messages);
        var contentRows = ContentRowCount(termHeight);

        if (contentWidth <= 0)
            return Math.Max(0, allLines.Length - contentRows);

        // Height-aware path: find the smallest scrollOffset at which windowStart == 0.
        // Equivalent to: how many flat lines from the end fill up contentRows terminal rows?
        var usedRows = 0;
        var count = 0;
        for (var i = allLines.Length - 1; i >= 0; i--)
        {
            var h = EstimateLineHeight(allLines[i], contentWidth);
            if (usedRows + h > contentRows) break;
            usedRows += h;
            count++;
        }
        return Math.Max(0, allLines.Length - count);
    }

    /// <summary>
    /// Returns the number of terminal rows a single flat markup line occupies at the
    /// given content width, accounting for word-wrap.
    /// </summary>
    internal static int EstimateLineHeight(string markupLine, int contentWidth) =>
        Math.Max(1, (VisibleLength(markupLine) + contentWidth - 1) / contentWidth);

    /// <summary>
    /// Returns the content row budget available inside the Progress panel for a given terminal height.
    /// </summary>
    internal static int ComputeLineBudget(int termHeight) =>
        Math.Max(4, termHeight - HeaderSize - InputSize - 3);

    /// <summary>
    /// Returns the number of message lines visible inside the Progress panel
    /// (budget minus 2 rows reserved for the above/below scroll hints).
    /// This is the exact scroll page size — one PgUp/PgDn moves this many lines.
    /// </summary>
    internal static int ContentRowCount(int termHeight) =>
        Math.Max(1, ComputeLineBudget(termHeight) - 2);

    /// <summary>
    /// Estimates the rendered row count for a message, accounting for line wrapping
    /// at the given panel content width. For <see cref="ShellMessageKind.Line"/> messages
    /// embedded newlines are treated as hard line breaks (each segment wraps independently).
    /// </summary>
    internal static int EstimateMessageHeight(ShellMessage msg, int contentWidth)
    {
        if (msg.Kind == ShellMessageKind.Line)
        {
            var total = 0;
            foreach (var line in msg.Markup.Split('\n'))
            {
                var len = VisibleLength(line);
                total += Math.Max(1, (len + contentWidth - 1) / contentWidth);
            }
            return Math.Max(1, total);
        }
        // Nested panel: 2 border rows, each content line wraps within (contentWidth - 4).
        var innerWidth = Math.Max(1, contentWidth - 4);
        var panelTotal = 2;
        foreach (var line in msg.Markup.Split('\n'))
        {
            var len = VisibleLength(line);
            panelTotal += Math.Max(1, (len + innerWidth - 1) / innerWidth);
        }
        return panelTotal;
    }

    /// <summary>
    /// Counts visible characters in a Spectre markup string by skipping tag sequences.
    /// Handles Spectre escapes: [[ → one '[', ]] → one ']'.
    /// </summary>
    internal static int VisibleLength(string markup)
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

    internal static IRenderable RenderMessage(ShellMessage msg)
    {
        if (msg.Kind == ShellMessageKind.Panel)
        {
            var panel = new Panel(CreateMarkupSafe(msg.Markup));
            if (msg.Title is not null)
            {
                var escapedTitle = Markup.Escape(msg.Title);
                panel.Header = msg.TitleJustify.HasValue
                    ? new PanelHeader(escapedTitle, msg.TitleJustify.Value)
                    : new PanelHeader(escapedTitle);
            }
            if (msg.BorderColor is not null) panel.BorderStyle = new Style(foreground: msg.BorderColor.Value);
            return panel;
        }
        return CreateMarkupSafe(msg.Markup);
    }

    private static Markup CreateMarkupSafe(string markup)
    {
        try
        {
            return new Markup(markup);
        }
        catch (InvalidOperationException)
        {
            return new Markup(Markup.Escape(markup));
        }
    }

    internal static string FormatAgentSlot(AgentSlot slot)
    {
        var icon = slot.Status == AgentRunStatus.Running ? "⚡" : "⏳";
        var role = slot.RoleSlug.Length > AgentRoleMax ? slot.RoleSlug[..(AgentRoleMax - 1)] + "…" : slot.RoleSlug;
        var title = slot.Title.Length > AgentTitleMax ? slot.Title[..(AgentTitleMax - 1)] + "…" : slot.Title;
        return $"[dim]{icon}[/] [cyan]{Markup.Escape(role)}[/] [dim]#{slot.IssueId}[/] {Markup.Escape(title)}";
    }

    internal static string FormatRoadmapSlot(RoadmapSlot slot)
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
