using System.Text;
using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli.Shell;

internal sealed partial class ShellService
{
    // ── Help text ──────────────────────────────────────────────────────────────

    private void AddInteractiveHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[bold]Interactive commands:[/]");
        sb.AppendLine("  [cyan]/init[/] \"goal text\" [--goal-file PATH] [--force] [--mode SLUG] [--keep-awake true|false]");
        sb.AppendLine("  [cyan]/customize[/] [--force]    Copy default assets to .devteam-source/ for editing");
        sb.AppendLine("  [cyan]/bug[/] [--save PATH] [--redact-paths true|false]");
        sb.AppendLine("  [cyan]/status[/]");
        sb.AppendLine("  [cyan]/history[/]              Show session command history (last 50)");
        sb.AppendLine("  [cyan]/mode[/] <slug>");
        sb.AppendLine("  [cyan]/keep-awake[/] <on|off>");
        sb.AppendLine("  [cyan]/add-issue[/] \"title\" --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--depends-on N ...]");
        sb.AppendLine("  [cyan]/plan[/]");
        sb.AppendLine("  [cyan]/questions[/]");
        sb.AppendLine("  [cyan]/budget[/] [--total N] [--premium N]");
        sb.AppendLine("  [cyan]/check-update[/]");
        sb.AppendLine("  [cyan]/update[/]");
        sb.AppendLine("  [cyan]/max-iterations[/] <N>    Set workspace default max iterations");
        sb.AppendLine("  [cyan]/max-subagents[/] <N>     Set workspace default max subagents (1=sequential, 2–4=parallel)");
        sb.AppendLine("  [cyan]/run[/] [--max-iterations N] [--max-subagents N] [--timeout-seconds N]  [dim]starts in background[/]");
        sb.AppendLine("  [cyan]/stop[/]              Cancel the running loop");
        sb.AppendLine("  [cyan]/wait[/]              Re-attach to the running loop and wait for it to finish");
        sb.AppendLine("  [cyan]/feedback[/] <text>");
        sb.AppendLine("  [cyan]/approve[/] [note]");
        sb.AppendLine("  [cyan]/answer[/] <id> <text>  [dim]works while the loop is running[/]");
        sb.AppendLine("  [cyan]/goal[/] <text> [--goal-file PATH]");
        sb.AppendLine("  [cyan]/exit[/]");
        sb.AppendLine();
        sb.AppendLine("If exactly one question is open, you can type a plain answer without /answer.");
        sb.AppendLine("While a plan is awaiting approval, plain text is treated as planning feedback.");
        sb.AppendLine();
        sb.AppendLine("[bold]Direct role invocation:[/]");
        sb.Append("  [cyan]@role[/] <message>    e.g. [dim]@architect can you review our API design?[/]");
        AddSystem(sb.ToString(), "help");
    }

    // ── Banner ─────────────────────────────────────────────────────────────────

    private void AddBanner(string workspacePath)
    {
        AddLine($"[bold cyan]─── DevTeam ───[/]");
        AddLine($"Workspace: [cyan]{Markup.Escape(workspacePath)}[/]  [dim]· /help for commands · /exit to quit[/]");
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

    private void AddSystem(string markup, string? header = null) =>
        AddMessage(new ShellMessage(ShellMessageKind.Panel, markup,
            Title: header ?? "devteam",
            BorderColor: Color.Grey));

    private void AddAgent(string roleSlug, string text, string? outcome = null)
    {
        var borderColor = outcome switch
        {
            "completed" => Color.Green,
            "blocked" => Color.Yellow,
            "failed" => Color.Red,
            _ => Color.Cyan,
        };
        var titleColor = borderColor;
        var headerText = outcome is not null ? $"{roleSlug} — {outcome}" : roleSlug;
        AddMessage(new ShellMessage(ShellMessageKind.Panel, Markup.Escape(text),
            Title: headerText,
            BorderColor: borderColor,
            TitleColor: titleColor));
    }

    private void AddQuestion(string questionText, int? questionId = null, bool isBlocking = true, int index = 1, int total = 1)
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

    private void AddError(string text) =>
        AddLine($"[bold red]✗[/] [red]{Markup.Escape(text)}[/]");

    private void AddHint(string markup) =>
        AddLine($"[dim]{markup}[/]");

    private void AddLoopLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var trimmed = message.TrimStart();
        var pad = new string(' ', message.Length - trimmed.Length);
        var escaped = Markup.Escape(trimmed);

        string markup;
        if (trimmed.StartsWith("Iteration ", StringComparison.OrdinalIgnoreCase))
            markup = $"\n[grey]{pad}── {escaped} ──[/]";
        else if (trimmed.StartsWith("Running issue #", StringComparison.OrdinalIgnoreCase)
              || trimmed.StartsWith("Bootstrapped:", StringComparison.OrdinalIgnoreCase))
            markup = $"[dim]{pad}→ {escaped}[/]";
        else if (trimmed.StartsWith("Still running", StringComparison.OrdinalIgnoreCase))
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

        AddMessage(new ShellMessage(ShellMessageKind.Line, markup));
    }

    private void AddHistory(string entry)
    {
        lock (_gate)
        {
            _history.Add((DateTimeOffset.Now, entry));
            if (_history.Count > 50) _history.RemoveAt(0);
        }
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();

    // ── CopyPackagedAssets ─────────────────────────────────────────────────────

    private void CopyPackagedAssets(string targetRoot, bool force)
    {
        // Walk up from the tool install directory to find packaged .devteam-source
        string? sourceRoot = null;
        var searchBase = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(searchBase); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, ".devteam-source");
            if (Directory.Exists(candidate))
            {
                sourceRoot = candidate;
                break;
            }
        }

        if (sourceRoot is null)
        {
            AddWarning("Could not locate packaged .devteam-source assets.");
            return;
        }

        var copied = 0;
        var skipped = 0;
        foreach (var srcFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, srcFile);
            var destFile = Path.Combine(targetRoot, relative);
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrWhiteSpace(destDir)) Directory.CreateDirectory(destDir);
            if (!force && File.Exists(destFile))
            {
                skipped++;
                continue;
            }
            File.Copy(srcFile, destFile, overwrite: true);
            copied++;
        }
        AddSuccess($"Copied {copied} asset(s) to {Markup.Escape(targetRoot)}{(skipped > 0 ? $" ({skipped} skipped — use --force to overwrite)" : "")}.");
    }
}
