using System.Linq;
using System.Text;
using System.Threading;
using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli.Shell;

internal sealed partial class ShellService
{
    // ── Help text ──────────────────────────────────────────────────────────────

    private void AddInteractiveHelp(bool showAll = false)
    {
        var markup = BuildInteractiveHelpMarkup(showAll);
        AddSystem(markup, "help");
    }

    /// <summary>
    /// Builds the interactive help markup string. Extracted for testability.
    /// </summary>
    internal static string BuildInteractiveHelpMarkup(bool showAll = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[bold]Interactive commands:[/]");
        sb.AppendLine("[dim]Typical flow: /init -> /plan -> feedback or /approve -> /run[/]");
        sb.AppendLine();
        sb.AppendLine("  [cyan]/init[/] \"goal text\" [[--goal-file PATH]] [[--force]] [[--mode SLUG]] [[--provider NAME]] [[--keep-awake true|false]]  [dim]Initialise workspace with a goal[/]");
        sb.AppendLine("  [cyan]/customize[/] [[--force]]    [dim]Copy default prompt assets to .devteam-source/ for editing[/]");
        sb.AppendLine("  [cyan]/export[/] [[--output PATH]]  [dim]Package the current workspace for handoff or backup[/]");
        sb.AppendLine("  [cyan]/import[/] --input PATH [[--force]]  [dim]Import a previously exported workspace archive[/]");
        sb.AppendLine("  [cyan]/start-here[/] [[new|medior|expert]]  [dim]Show the guided onboarding flow for your persona[/]");
        sb.AppendLine("  [cyan]/bug[/] [[--save PATH]] [[--redact-paths true|false]]  [dim]Generate a sanitized bug report draft[/]");
        sb.AppendLine("  [cyan]/status[/]               [dim]Show workspace phase, open issues, and stall state[/]");
        sb.AppendLine("  [cyan]/history[/]              [dim]Show session command history (last 50)[/]");
        sb.AppendLine("  [cyan]/mode[/] <slug>          [dim]Switch the active run mode[/]");
        sb.AppendLine("  [cyan]/pipeline[/]             [dim]Show the current default role chain[/]");
        sb.AppendLine("  [cyan]/set-pipeline[/] <role ...|default>  [dim]Customize or reset the default role chain[/]");
        sb.AppendLine("  [cyan]/provider[/]             [dim]Show the current BYOK provider override[/]");
        sb.AppendLine("  [cyan]/set-provider[/] <name|default>  [dim]Set or reset the default BYOK provider[/]");
        sb.AppendLine("  [cyan]/keep-awake[/] <on|off>  [dim]Prevent system sleep during long runs[/]");
        sb.AppendLine("  [cyan]/add-issue[/] \"title\" --role ROLE [[--area AREA]] [[--detail TEXT]] [[--priority N]] [[--depends-on N ...]]  [dim]Queue a new issue[/]");
        sb.AppendLine("  [cyan]/edit-issue[/] <id> [[--title TEXT]] [[--detail TEXT]] [[--role ROLE]] [[--area AREA|--clear-area]] [[--priority N]] [[--status STATE]] [[--depends-on N ...|--clear-depends]]  [dim]Edit a queued issue safely[/]");
        sb.AppendLine("  [cyan]/plan[/] [[--provider NAME]]  [dim]Show or generate the current plan[/]");
        sb.AppendLine("  [cyan]/diff-run[/] <run-id> [[compare-run-id]]  [dim]Show what a run changed, or compare two runs[/]");
        sb.AppendLine("  [cyan]/brownfield-log[/]       [dim]Show the brownfield before/after audit log[/]");
        sb.AppendLine("  [cyan]/sync[/]                 [dim]Pull GitHub-labelled issues into the local workspace[/]");
        sb.AppendLine("  [cyan]/questions[/]            [dim]List open questions with age and blocking state[/]");
        sb.AppendLine("  [cyan]/budget[/] [[--total N]] [[--premium N]]  [dim]View or adjust the credit budget[/]");
        sb.AppendLine("  [cyan]/check-update[/]         [dim]Check for a newer version of DevTeam[/]");
        sb.AppendLine("  [cyan]/update[/]               [dim]Update DevTeam to the latest version[/]");
        sb.AppendLine("  [cyan]/max-iterations[/] <N>   [dim]Set workspace default max iterations per loop[/]");
        sb.AppendLine("  [cyan]/max-subagents[/] <N>    [dim]Set workspace default max subagents (1=sequential, 2–4=parallel)[/]");
        sb.AppendLine("  [cyan]/worktrees[/] <on|off>   [dim]Enable/disable git worktree isolation for parallel runs[/]");
        sb.AppendLine("  [cyan]/recon[/] [[--backend B]] [[--timeout-seconds N]]  [dim]Re-run codebase reconnaissance and update context[/]");
        sb.AppendLine("  [cyan]/preview[/] [[--max-subagents N]]  [dim]Preview the next batch without spending credits[/]");
        sb.AppendLine("  [cyan]/run[/] [[--provider NAME]] [[--max-iterations N]] [[--max-subagents N]] [[--timeout-seconds N]] [[--dry-run]]  [dim]Start the loop in the background[/]");
        sb.AppendLine("  [cyan]/stop[/]                 [dim]Cancel the running loop[/]");
        sb.AppendLine("  [cyan]/connect[/] [[slug [[issue-id]] | slug#issue-id | issue-id]]  [dim]Stream live output from one running agent[/]");
        sb.AppendLine("  [cyan]/connect-to[/] [[slug [[issue-id]] | slug#issue-id | issue-id]]  [dim]Alias for /connect[/]");
        sb.AppendLine("  [cyan]/disconnect[/]           [dim]Stop streaming agent output[/]");
        sb.AppendLine("  [cyan]/wait[/]                 [dim]Re-attach to the running loop and wait for it to finish[/]");
        sb.AppendLine("  [cyan]/feedback[/] <text>      [dim]Add planning feedback[/]");
        sb.AppendLine("  [cyan]/approve[/] [[note]]     [dim]Approve the current plan and advance to the next phase[/]");
        sb.AppendLine("  [cyan]/answer[/] <id> <text>   [dim]Answer an open question (works while the loop is running)[/]");
        sb.AppendLine("  [cyan]/goal[/] <text> [[--goal-file PATH]]  [dim]Set or update the active goal[/]");
        sb.AppendLine("  [cyan]/exit[/]                 [dim]Exit the shell[/]");
        sb.AppendLine();
        sb.AppendLine("[bold]Smart input:[/]");
        sb.AppendLine("  If exactly one question is open, you can type a plain answer without /answer.");
        sb.AppendLine("  While a plan is awaiting approval, plain text is treated as planning feedback.");
        sb.AppendLine();
        sb.AppendLine("[bold]Direct role invocation:[/]");
        sb.Append("  [cyan]@role[/] <message>    e.g. [dim]@architect can you review our API design?[/]");
        if (showAll)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("[bold]Hidden extras:[/]");
            sb.AppendLine("  [cyan]/adventure[/] [[on|off]]  [dim]Toggle the ASCII office explorer mode[/]");
            sb.Append("  [dim]While active: arrow keys move, Enter chats with a nearby desk, Esc returns to the normal shell.[/]");
        }

        return sb.ToString();
    }

    internal static string? BuildWorkflowGuideMarkup(WorkspaceState state, bool isLoopRunning, int readyIssueCount)
    {
        var openQuestionCount = state.Questions.Count(q => q.Status == QuestionStatus.Open);
        if (openQuestionCount > 0 && !isLoopRunning)
        {
            return null;
        }

        if (isLoopRunning)
        {
            var loopHeader = state.Phase switch
            {
                WorkflowPhase.Planning => "[bold]Step 1 of 3 - planning in progress[/]",
                WorkflowPhase.ArchitectPlanning => "[bold]Step 2 of 3 - architecture in progress[/]",
                WorkflowPhase.Execution => "[bold]Step 3 of 3 - execution in progress[/]",
                _ => "[bold]Loop in progress[/]"
            };

            return loopHeader + "\n" +
                "Use [cyan]/status[/] to inspect progress, [cyan]/answer[/] to unblock questions, [cyan]/stop[/] to request a stop, or [cyan]/wait[/] to stay attached.";
        }

        var planGenerated = state.Issues.Any(i => i.IsPlanningIssue && i.Status == ItemStatus.Done);
        var architectAwaitingApproval = PlanWorkflow.IsAwaitingArchitectApproval(state);

        if (state.Phase == WorkflowPhase.Planning && !planGenerated)
        {
            return "[bold]Step 1 of 3 - planning[/]\n" +
                "Run [cyan]/plan[/] to generate the first high-level plan. Then review it here and either type feedback as plain text or use [cyan]/approve[/].";
        }

        if (state.Phase == WorkflowPhase.Planning)
        {
            return "[bold]Step 1 of 3 - plan review[/]\n" +
                "Your high-level plan is ready. Type feedback as plain text to revise it, or use [cyan]/approve[/] to move into architecture. Use [dim]/plan[/] to review it again.";
        }

        if (state.Phase == WorkflowPhase.ArchitectPlanning && !architectAwaitingApproval)
        {
            return "[bold]Step 2 of 3 - architecture[/]\n" +
                "Run [cyan]/run[/] to let the architect turn the approved plan into concrete execution issues. Then review the result with [dim]/plan[/] and [cyan]/approve[/] again.";
        }

        if (architectAwaitingApproval)
        {
            return "[bold]Step 2 of 3 - architect review[/]\n" +
                "The architect plan is ready. Type feedback as plain text to revise it, or use [cyan]/approve[/] to begin execution.";
        }

        if (state.Phase == WorkflowPhase.Execution)
        {
            if (readyIssueCount > 0)
            {
                return $"[bold]Step 3 of 3 - execution[/]\n" +
                    $"[bold]{readyIssueCount}[/] issue(s) are ready. Use [cyan]/run[/] to start work. If you are learning the loop, [cyan]/max-subagents 1[/] is the safest default; [cyan]2-3[/] is a good standard setting.";
            }

            return "[bold]Step 3 of 3 - execution[/]\n" +
                "There is no ready work right now. Use [cyan]/status[/] to inspect the board, [cyan]/questions[/] to review open questions, or [cyan]/add-issue[/] to queue work manually.";
        }

        return null;
    }

    // ── Banner ─────────────────────────────────────────────────────────────────

    private void AddBanner(string workspacePath)
    {
        // Workspace path can be long on CI / deep clones. Keep the hint on its
        // own line so it is never truncated regardless of terminal width.
        AddLine($"Workspace: [cyan]{Markup.Escape(workspacePath)}[/]");
        AddLine("[dim]· /help for commands · /exit to quit[/]");
    }

    /// <summary>
    /// If the workspace has open sprint work from a previous session, show a resume prompt.
    /// </summary>
    internal void ShowSprintResumeHint(WorkspaceState state)
    {
        if (state.Phase == WorkflowPhase.Planning && !state.Issues.Any(i => i.Status == ItemStatus.Open || i.Status == ItemStatus.InProgress))
            return;

        var activeIssues = state.Issues
            .Where(i => !i.IsPlanningIssue && i.Status is ItemStatus.Open or ItemStatus.InProgress)
            .ToList();

        if (activeIssues.Count == 0)
            return;

        var inProgress = activeIssues.Where(i => i.Status == ItemStatus.InProgress).ToList();

        var phaseName = state.Phase switch
        {
            WorkflowPhase.ArchitectPlanning => "architect planning",
            WorkflowPhase.Execution => "execution",
            _ => state.Phase.ToString().ToLowerInvariant()
        };

        if (inProgress.Count > 0)
        {
            AddWarning($"{inProgress.Count} issue(s) were still in progress when the last sprint stopped. They will be retried — use /run to continue.");
        }

        AddHint($"↩ [bold]{activeIssues.Count}[/] sprint item(s) waiting in [bold]{phaseName}[/] phase. Use [cyan]/run[/] to resume or [cyan]/status[/] to review.");
    }

    // ── Message factories ──────────────────────────────────────────────────────

    private void AddMessage(ShellMessage msg)
    {
        lock (_gate)
        {
            _messages.Add(msg);
            // Trim to keep memory bounded; visible window is managed in the component.
            if (_messages.Count > 500)
                _messages.RemoveRange(0, _messages.Count - 500);
        }
        NotifyStateChanged();
    }

    private void AddLine(string markup) =>
        AddMessage(new ShellMessage(ShellMessageKind.Line, markup));

    /// <summary>
    /// Adds a system panel, automatically splitting content that exceeds
    /// <see cref="MaxPanelChunkLines"/> lines into consecutive numbered chunks.
    /// </summary>
    private void AddSystem(string markup, string? header = null)
    {
        const int MaxPanelChunkLines = 15;
        var lines = markup.Split('\n');
        if (lines.Length <= MaxPanelChunkLines)
        {
            AddMessage(new ShellMessage(ShellMessageKind.Panel, markup,
                Title: header ?? "devteam",
                BorderColor: Color.Grey));
            return;
        }
        var totalChunks = (lines.Length + MaxPanelChunkLines - 1) / MaxPanelChunkLines;
        for (var c = 0; c < totalChunks; c++)
        {
            var chunk = string.Join("\n", lines.Skip(c * MaxPanelChunkLines).Take(MaxPanelChunkLines));
            var title = $"{header ?? "devteam"} ({c + 1}/{totalChunks})";
            AddMessage(new ShellMessage(ShellMessageKind.Panel, chunk,
                Title: title,
                BorderColor: Color.Grey));
        }
    }

    /// <summary>
    /// Adds an agent panel, splitting large summaries into chunks the same way as
    /// <see cref="AddSystem"/>.
    /// </summary>
    private void AddAgent(string roleSlug, string text, string? outcome = null)
    {
        const int MaxPanelChunkLines = 20;
        var borderColor = outcome switch
        {
            "completed" => Color.Green,
            "blocked" => Color.Yellow,
            "failed" => Color.Red,
            _ => Color.Cyan,
        };
        var titleColor = borderColor;
        var escaped = Markup.Escape(text);
        var lines = escaped.Split('\n');
        lock (_gate)
        {
            _adventureSpeechBubbles[roleSlug] = BuildAdventureBubbleText(text);
        }

        if (lines.Length <= MaxPanelChunkLines)
        {
            var headerText = outcome is not null ? $"{roleSlug} — {outcome}" : roleSlug;
            AddMessage(new ShellMessage(ShellMessageKind.Panel, escaped,
                Title: headerText,
                BorderColor: borderColor,
                TitleColor: titleColor));
            return;
        }
        var totalChunks = (lines.Length + MaxPanelChunkLines - 1) / MaxPanelChunkLines;
        for (var c = 0; c < totalChunks; c++)
        {
            var chunk = string.Join("\n", lines.Skip(c * MaxPanelChunkLines).Take(MaxPanelChunkLines));
            var suffix = outcome is not null ? $" — {outcome}" : "";
            var chunkTitle = $"{roleSlug}{suffix} ({c + 1}/{totalChunks})";
            AddMessage(new ShellMessage(ShellMessageKind.Panel, chunk,
                Title: chunkTitle,
                BorderColor: borderColor,
                TitleColor: titleColor));
        }
    }

    private void AddQuestion(string questionText, bool isBlocking = true, int index = 1, int total = 1)
    {
        var counter = total > 1 ? $" ({index}/{total})" : "";
        var blocking = isBlocking ? " blocking" : "";
        AddMessage(new ShellMessage(ShellMessageKind.Panel, $"[white]{Markup.Escape(questionText)}[/]",
            Title: $"question{counter}{blocking}",
            BorderColor: Color.Yellow,
            TitleColor: Color.Yellow));
    }

    private void AddEvent(string icon, string markup, string style = "dim") =>
        AddLine($"[{style}]{Markup.Escape(icon)} {markup}[/]");

    private void AddSuccess(string text) =>
        AddLine($"[bold green]✓[/] [green]{Markup.Escape(text)}[/]");

    private void AddWarning(string text) =>
        AddLine($"[bold yellow]⚠[/] [yellow]{Markup.Escape(text)}[/]");

    internal void AddError(string text) =>
        AddLine($"[bold red]✗[/] [red]{Markup.Escape(text)}[/]");

    private void AddHint(string markup) =>
        AddLine($"[dim]{markup}[/]");

    private void AddLoopLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var trimmed = message.TrimStart();
        var pad = new string(' ', message.Length - trimmed.Length);
        var escaped = Markup.Escape(trimmed);

        bool isHeartbeat = trimmed.StartsWith("Still running", StringComparison.OrdinalIgnoreCase);

        string markup;
        if (trimmed.StartsWith("Iteration ", StringComparison.OrdinalIgnoreCase))
            markup = $"\n[grey]{pad}── {escaped} ──[/]";
        else if (trimmed.StartsWith("Running issue #", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith("Bootstrapped:", StringComparison.OrdinalIgnoreCase))
            markup = $"[dim]{pad}→ {escaped}[/]";
        else if (isHeartbeat)
            markup = $"[dim]{pad}⏳ {escaped}[/]";
        else if (trimmed.StartsWith("Outcome:", StringComparison.OrdinalIgnoreCase))
        {
            var outcome = trimmed["Outcome:".Length..].Trim();
            var color = outcome switch { "completed" => "green", "blocked" => "yellow", "failed" => "red", _ => "grey" };
            markup = $"[{color}]{pad}  ✓ outcome: {Markup.Escape(outcome)}[/]";
        }
        else if (trimmed.StartsWith("Budget", StringComparison.OrdinalIgnoreCase))
            markup = $"[dim]{pad}💰 {escaped}[/]";
        else
            markup = $"[dim]{pad}{escaped}[/]";

        var msg = new ShellMessage(ShellMessageKind.Line, markup, IsHeartbeat: isHeartbeat);

        if (isHeartbeat)
            ReplaceLastHeartbeat(msg);
        else
            AddMessage(msg);
    }

    /// <summary>Replace the most recent heartbeat message instead of appending,
    /// so "Still running …" lines don't pile up and push real content off-screen.</summary>
    private void ReplaceLastHeartbeat(ShellMessage msg)
    {
        lock (_gate)
        {
            for (var i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].IsHeartbeat)
                {
                    _messages[i] = msg;
                    NotifyStateChanged();
                    return;
                }
            }
            _messages.Add(msg);
        }
        NotifyStateChanged();
    }

    private void AddHistory(string entry)
    {
        lock (_gate)
        {
            _history.Add((_clock.UtcNow, entry));
            if (_history.Count > 50) _history.RemoveAt(0);
        }
    }

    private void NotifyStateChanged()
    {
        Interlocked.Increment(ref _uiVersion);
        OnStateChanged?.Invoke();
    }

}
