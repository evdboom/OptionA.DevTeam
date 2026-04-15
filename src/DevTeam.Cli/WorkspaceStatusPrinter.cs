using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli;

internal static class WorkspaceStatusPrinter
{
    internal static void PrintStatus(WorkspaceState state, DevTeamRuntime runtime)
    {
        var report = runtime.BuildStatusReport(state);

        AnsiConsole.MarkupLine($"[bold]Phase:[/] [cyan]{Markup.Escape(state.Phase.ToString())}[/]  " +
            $"[bold]Mode:[/] [cyan]{Markup.Escape(state.Runtime.ActiveModeSlug)}[/]  " +
            $"[bold]Max-iter:[/] {state.Runtime.DefaultMaxIterations}  " +
            $"[bold]Max-sub:[/] {state.Runtime.DefaultMaxSubagents}");

        var issues = state.Issues.OrderBy(i => i.Id).ToList();
        if (issues.Count > 0)
        {
            var table = new Table { Border = TableBorder.Rounded, Expand = false };
            table.AddColumn(new TableColumn("[dim]#[/]").RightAligned());
            table.AddColumn("[dim]Role[/]");
            table.AddColumn("[dim]Status[/]");
            table.AddColumn("[dim]Title[/]");
            table.AddColumn(new TableColumn("[dim]Pri[/]").RightAligned());
            table.AddColumn("[dim]Pipeline[/]");

            foreach (var issue in issues)
            {
                var statusColor = issue.Status switch
                {
                    ItemStatus.Done => "green",
                    ItemStatus.InProgress => "yellow",
                    ItemStatus.Blocked => "red",
                    _ => "dim"
                };
                var title = issue.Title.Length > 60 ? issue.Title[..57] + "..." : issue.Title;
                var pipelineCell = FormatPipelineCell(state, issue);
                table.AddRow(
                    $"[dim]{issue.Id}[/]",
                    $"[cyan]{Markup.Escape(issue.RoleSlug)}[/]",
                    $"[{statusColor}]{issue.Status}[/]",
                    Markup.Escape(title),
                    $"[dim]{issue.Priority}[/]",
                    pipelineCell);
            }
            AnsiConsole.Write(table);
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]No issues.[/]");
        }

        AnsiConsole.MarkupLine(
            $"[bold]Budget:[/] {report.Budget.CreditsCommitted:0.##}/{report.Budget.TotalCreditCap} credits " +
            $"[dim]({report.Budget.TotalCreditCap - report.Budget.CreditsCommitted:0.##} remaining)[/]  " +
            $"premium {report.Budget.PremiumCreditsCommitted:0.##}/{report.Budget.PremiumCreditCap} " +
            $"[dim]({report.Budget.PremiumCreditCap - report.Budget.PremiumCreditsCommitted:0.##} remaining)[/]");

        if (report.OpenQuestions.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[bold yellow]{report.OpenQuestions.Count} open question(s)[/]");
            foreach (var q in report.OpenQuestions)
            {
                var tag = q.IsBlocking ? "[yellow]blocking[/]" : "[dim]non-blocking[/]";
                AnsiConsole.MarkupLine($"  [dim]#{q.Id}[/] [{tag}] {Markup.Escape(q.Text)}");
            }
        }

        if (report.QueuedRuns.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[bold]{report.QueuedRuns.Count} queued/running run(s)[/]");
            foreach (var run in report.QueuedRuns)
            {
                AnsiConsole.MarkupLine($"  [dim]#{run.Id}[/] issue [dim]#{run.IssueId}[/] [cyan]{Markup.Escape(run.RoleSlug)}[/] via {Markup.Escape(run.ModelName)}");
            }
        }
    }

    internal static string FormatPipelineCell(WorkspaceState state, IssueItem issue)
    {
        if (issue.PipelineId is null)
        {
            return "[dim]—[/]";
        }
        var pipeline = state.Pipelines.FirstOrDefault(p => p.Id == issue.PipelineId);
        if (pipeline is null)
        {
            return $"[dim]#{issue.PipelineId}[/]";
        }
        var stageIndex = issue.PipelineStageIndex ?? 0;
        var roles = pipeline.RoleSequence;
        var parts = new List<string>();
        for (var i = 0; i < roles.Count; i++)
        {
            var r = Markup.Escape(roles[i]);
            if (i == stageIndex)
                parts.Add($"[bold cyan]{r}[/]");
            else if (i < stageIndex)
                parts.Add($"[green]{r}[/]");
            else
                parts.Add($"[dim]{r}[/]");
        }
        return $"[dim]#{pipeline.Id}[/] {string.Join(" → ", parts)}";
    }

    internal static void PrintBudget(BudgetState budget)
    {
        Console.WriteLine(
            $"{ConsoleTheme.Label("Budget:")} {ConsoleTheme.BudgetUsage(budget.CreditsCommitted, budget.TotalCreditCap)} credits " +
            $"({ConsoleTheme.Number($"{budget.TotalCreditCap - budget.CreditsCommitted:0.##}")} remaining), " +
            $"premium {ConsoleTheme.BudgetUsage(budget.PremiumCreditsCommitted, budget.PremiumCreditCap)} " +
            $"({ConsoleTheme.Number($"{budget.PremiumCreditCap - budget.PremiumCreditsCommitted:0.##}")} remaining)");
    }

    internal static bool PrintArchitectSummary(WorkspaceState state)
    {
        var architectRuns = state.AgentRuns
            .Where(run => string.Equals(run.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
                && run.Status == AgentRunStatus.Completed
                && !string.IsNullOrWhiteSpace(run.Summary))
            .OrderByDescending(run => run.UpdatedAtUtc)
            .Take(1)
            .ToList();
        if (architectRuns.Count == 0)
        {
            return false;
        }

        Console.WriteLine();
        Console.WriteLine(ConsoleTheme.Label("─── Architect Summary ───"));
        foreach (var run in architectRuns)
        {
            var issue = state.Issues.FirstOrDefault(i => i.Id == run.IssueId);
            if (issue is not null)
            {
                Console.WriteLine($"  {ConsoleTheme.Accent(issue.Title)}");
            }
            foreach (var summaryLine in run.Summary.Split('\n'))
            {
                Console.WriteLine($"  {summaryLine}");
            }
        }

        var createdIssues = state.Issues
            .Where(i => !i.IsPlanningIssue
                && !string.Equals(i.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
                && i.Status != ItemStatus.Done)
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Id)
            .ToList();
        if (createdIssues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(ConsoleTheme.Label($"Execution issues created ({createdIssues.Count}):"));
            foreach (var issue in createdIssues)
            {
                Console.WriteLine($"  #{ConsoleTheme.Number(issue.Id.ToString())} [{ConsoleTheme.Role(issue.RoleSlug)}] {issue.Title}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Use {ConsoleTheme.Command("/approve")} to begin execution or type feedback to revise.");
        return true;
    }

    internal static void PrintQuestions(WorkspaceState state, WorkspaceStore store)
    {
        var openQuestions = state.Questions
            .Where(item => item.Status == QuestionStatus.Open)
            .OrderBy(item => item.Id)
            .ToList();
        Console.WriteLine($"Questions file: {Path.Combine(store.WorkspacePath, "questions.md")}");
        if (openQuestions.Count == 0)
        {
            Console.WriteLine("No open questions.");
            return;
        }

        foreach (var question in openQuestions)
        {
            Console.WriteLine($"#{question.Id} [{(question.IsBlocking ? "blocking" : "non-blocking")}] {question.Text}");
        }
    }

    internal static void PrintOpenQuestions(WorkspaceState state, bool interactive)
    {
        var openQuestions = state.Questions
            .Where(item => item.Status == QuestionStatus.Open)
            .OrderBy(item => item.Id)
            .ToList();

        if (openQuestions.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(ConsoleTheme.Label($"─── {openQuestions.Count} open question{(openQuestions.Count == 1 ? "" : "s")} ───"));
        foreach (var question in openQuestions)
        {
            var tag = question.IsBlocking ? ConsoleTheme.Warning("blocking") : ConsoleTheme.Muted("non-blocking");
            Console.WriteLine($"  #{ConsoleTheme.Number(question.Id.ToString())} [{tag}] {question.Text}");
        }
        Console.WriteLine(interactive
            ? $"Answer with: {ConsoleTheme.Command("/answer")} <id> <text>"
            : "Answer with: answer-question <id> <text>");
        Console.WriteLine();
    }

    internal static void PrintPlan(WorkspaceStore store)
    {
        var path = Path.Combine(store.WorkspacePath, "plan.md");
        if (!File.Exists(path))
        {
            ChatConsole.WriteHint("No plan has been written yet.");
            return;
        }

        var content = File.ReadAllText(path).TrimEnd();
        ChatConsole.WriteSystem(Markup.Escape(content), "plan");
    }

    internal static void PrintLoopResult(LoopResult result)
    {
        Console.WriteLine($"Loop state: {result.State}");
        if (result.Created.Count > 0)
        {
            Console.WriteLine($"Bootstrapped: {string.Join(", ", result.Created)}");
        }
        foreach (var run in result.QueuedRuns)
        {
            Console.WriteLine($"Queued run #{run.RunId} for issue #{run.IssueId} ({run.RoleSlug} via {run.ModelName}): {run.Title}");
        }
    }

    internal static void PrintHelp()
    {
        Console.WriteLine("DevTeam CLI");
        Console.WriteLine("Commands (plain or slash-prefixed, for example `/init`):");
        Console.WriteLine("  start [--keep-awake true|false] [--workspace PATH]");
        Console.WriteLine("  init [--force] [--workspace PATH] [--goal TEXT | --goal-file PATH] [--mode SLUG] [--keep-awake true|false] [--total-credit-cap N] [--premium-credit-cap N] [--workspace-mcp true|false] [--pipeline-scheduling true|false] [--recon true|false] [--backend sdk|cli] [--timeout-seconds N]");
        Console.WriteLine("  customize [--force]                Copy default roles, modes, and superpowers to .devteam-source/ for editing");
        Console.WriteLine("  bug-report [--save PATH] [--redact-paths true|false] [--history-count N] [--error-count N] [--workspace PATH]");
        Console.WriteLine("  set-goal <TEXT> [--goal-file PATH] [--workspace PATH]");
        Console.WriteLine("  set-mode <SLUG> [--workspace PATH]");
        Console.WriteLine("  set-keep-awake <true|false> [--workspace PATH]");
        Console.WriteLine("  set-auto-approve <true|false> [--workspace PATH]");
        Console.WriteLine("  max-iterations <N> [--workspace PATH]    Set workspace default for max loop iterations");
        Console.WriteLine("  max-subagents <N> [--workspace PATH]     Set workspace default for max parallel subagents");
        Console.WriteLine("  add-roadmap <TITLE> [--detail TEXT] [--priority N] [--workspace PATH]");
        Console.WriteLine("  add-issue <TITLE> --role ROLE [--area AREA] [--detail TEXT] [--priority N] [--roadmap-item-id N] [--depends-on N [N...]] [--workspace PATH]");
        Console.WriteLine("  add-question <TEXT> [--blocking] [--workspace PATH]");
        Console.WriteLine("  answer-question <ID> <ANSWER> [--workspace PATH]");
        Console.WriteLine("  approve-plan [--note TEXT] [--workspace PATH]");
        Console.WriteLine("  feedback <TEXT> [--workspace PATH]");
        Console.WriteLine("  questions [--workspace PATH]");
        Console.WriteLine("  plan [--workspace PATH]");
        Console.WriteLine("  budget [--total N] [--premium N] [--workspace PATH]");
        Console.WriteLine("  check-update");
        Console.WriteLine("  update");
        Console.WriteLine("  run-once [--max-subagents N] [--workspace PATH]");
        Console.WriteLine("  run-loop [--backend sdk|cli] [--max-iterations N] [--max-subagents N] [--timeout-seconds N] [--verbosity quiet|normal|detailed] [--keep-awake true|false] [--workspace PATH]");
        Console.WriteLine("  complete-run --run-id N --outcome completed|failed|blocked --summary TEXT [--workspace PATH]");
        Console.WriteLine("  status [--workspace PATH]");
        Console.WriteLine("  agent-invoke [--backend sdk|cli] [--prompt TEXT] [--model NAME] [--timeout-seconds N] [--working-directory PATH] [--extra-arg ARG ...]");
        Console.WriteLine("  workspace-mcp --workspace PATH");
    }

    internal static void PrintInteractiveHelp()
    {
        Console.WriteLine(ConsoleTheme.Label("Interactive commands:"));
        Console.WriteLine($"  {ConsoleTheme.Command("/init")} \"goal text\" [[--goal-file PATH]] [[--force]] [[--mode SLUG]] [[--keep-awake true|false]]");
        Console.WriteLine($"  {ConsoleTheme.Command("/customize")} [[--force]]    Copy default assets to .devteam-source/ for editing");
        Console.WriteLine($"  {ConsoleTheme.Command("/bug")} [[--save PATH]] [[--redact-paths true|false]]");
        Console.WriteLine($"  {ConsoleTheme.Command("/status")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/history")}              Show session command history (last 50)");
        Console.WriteLine($"  {ConsoleTheme.Command("/mode")} <slug>");
        Console.WriteLine($"  {ConsoleTheme.Command("/keep-awake")} <on|off>");
        Console.WriteLine($"  {ConsoleTheme.Command("/add-issue")} \"title\" --role ROLE [[--area AREA]] [[--detail TEXT]] [[--priority N]] [[--roadmap-item-id N]] [[--depends-on N [[N...]]]]");
        Console.WriteLine($"  {ConsoleTheme.Command("/plan")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/questions")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/budget")} [[--total N]] [[--premium N]]");
        Console.WriteLine($"  {ConsoleTheme.Command("/check-update")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/update")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/max-iterations")} <N>    Set workspace default max iterations (used by all future /run calls)");
        Console.WriteLine($"  {ConsoleTheme.Command("/max-subagents")} <N>     Set workspace default max subagents (1=sequential, 2–4=parallel)");
        Console.WriteLine($"  {ConsoleTheme.Command("/run")} [[--max-iterations N]] [[--max-subagents N]] [[--timeout-seconds N]] [[--keep-awake true|false]]  {ConsoleTheme.Muted("starts in background — shell stays responsive")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/stop")}              Cancel the running loop (waits for current agent call to finish)");
        Console.WriteLine($"  {ConsoleTheme.Command("/wait")}              Re-attach to the running loop and wait for it to finish");
        Console.WriteLine($"  {ConsoleTheme.Command("/feedback")} <text>");
        Console.WriteLine($"  {ConsoleTheme.Command("/approve")} [[note]]");
        Console.WriteLine($"  {ConsoleTheme.Command("/answer")} <id> <text>  {ConsoleTheme.Muted("works while the loop is running")}");
        Console.WriteLine($"  {ConsoleTheme.Command("/goal")} <text> [[--goal-file PATH]]");
        Console.WriteLine($"  {ConsoleTheme.Command("/exit")}");
        Console.WriteLine("If exactly one question is open, you can type a plain answer without `/answer`.");
        Console.WriteLine("While a plan is awaiting approval, plain text is treated as planning feedback and re-runs planning.");
        Console.WriteLine($"{ConsoleTheme.Muted("/answer")}, {ConsoleTheme.Muted("/questions")}, {ConsoleTheme.Muted("/status")}, {ConsoleTheme.Muted("/budget")}, and {ConsoleTheme.Muted("@role")} messages are all safe to use while the loop runs.");
        Console.WriteLine("If no plan exists yet, `/plan` runs the planner and then shows the generated plan.");
        Console.WriteLine();
        Console.WriteLine(ConsoleTheme.Label("Direct role invocation:"));
        Console.WriteLine($"  {ConsoleTheme.Command("@role")} <message>    Talk directly to any role (e.g. {ConsoleTheme.Muted("@architect can you review our API design?")})");
        Console.WriteLine($"  Roles: use {ConsoleTheme.Command("/status")} to see available roles. Tab-completes after {ConsoleTheme.Muted("@")}.");
    }
}
