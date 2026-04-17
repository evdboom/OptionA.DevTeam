using System.Text;
using DevTeam.Cli.Shell;
using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli;

internal static class RunPreviewPrinter
{
    internal static void PrintPreview(WorkspaceState state, DevTeamRuntime runtime, int maxSubagents)
    {
        var markup = BuildPreviewMarkup(state, runtime, maxSubagents);
        Console.WriteLine(NonInteractiveShellHost.StripMarkup(markup));
    }

    internal static string BuildPreviewMarkup(WorkspaceState state, DevTeamRuntime runtime, int maxSubagents)
    {
        var pendingRuns = GetPendingRuns(state, maxSubagents);
        var previewRuns = pendingRuns.Count > 0
            ? pendingRuns
            : runtime.BuildRunPreview(state, maxSubagents);
        var hasPendingRuns = pendingRuns.Count > 0;

        var sb = new StringBuilder();
        sb.AppendLine("[bold]Run preview[/]");
        sb.AppendLine($"[dim]max-subagents: {maxSubagents}[/]");

        if (previewRuns.Count == 0)
        {
            var finalState = runtime.GetLoopStateWhenNoReadyWork(state);
            if (finalState == "waiting-for-user")
            {
                sb.AppendLine("No batch is ready because the workspace is waiting for user input.");
                sb.AppendLine("Use [cyan]/questions[/] or [cyan]/answer[/] to unblock the loop.");
            }
            else
            {
                sb.AppendLine("No issues are ready to run right now.");
                sb.AppendLine("Use [cyan]/status[/] to inspect the board.");
            }

            return sb.ToString().TrimEnd();
        }

        double totalCredits = 0;
        double totalPremiumCredits = 0;
        foreach (var run in previewRuns)
        {
            var model = state.Models.FirstOrDefault(item => string.Equals(item.Name, run.ModelName, StringComparison.OrdinalIgnoreCase));
            var credits = model?.Cost ?? 0;
            totalCredits += credits;
            if (model?.IsPremium == true)
            {
                totalPremiumCredits += credits;
            }

            var areaText = string.IsNullOrWhiteSpace(run.Area) ? "" : $" [dim]@ {Markup.Escape(run.Area)}[/]";
            sb.AppendLine($"  [bold]#{run.IssueId}[/] [cyan]{Markup.Escape(run.RoleSlug)}[/]{areaText} {Markup.Escape(run.Title)}");
            sb.AppendLine($"    [dim]{Markup.Escape(run.ModelName)}[/] · est. [bold]{credits:0.##}[/] credit{(Math.Abs(credits - 1) < 0.001 ? "" : "s")}");
        }

        sb.AppendLine();
        sb.AppendLine($"Estimated batch cost: [bold]{totalCredits:0.##}[/] credits");
        sb.AppendLine($"Budget after batch: [bold]{state.Budget.CreditsCommitted + totalCredits:0.##}/{state.Budget.TotalCreditCap:0.##}[/] total");
        if (totalPremiumCredits > 0)
        {
            sb.AppendLine($"Premium after batch: [bold]{state.Budget.PremiumCreditsCommitted + totalPremiumCredits:0.##}/{state.Budget.PremiumCreditCap:0.##}[/]");
        }

        if (hasPendingRuns)
        {
            sb.AppendLine("[dim]These runs are already queued or running. /run will resume them.[/]");
        }
        else if (state.Phase == WorkflowPhase.Execution)
        {
            sb.AppendLine("[dim]This is a heuristic preview. The execution orchestrator may adjust the final batch at run time.[/]");
        }

        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<QueuedRunInfo> GetPendingRuns(WorkspaceState state, int maxSubagents)
    {
        var candidateRuns = state.AgentRuns
            .Where(run => run.Status is AgentRunStatus.Running or AgentRunStatus.Queued)
            .OrderByDescending(run => run.Status == AgentRunStatus.Running)
            .ThenBy(run => run.Id)
            .ToList();

        var selected = new List<QueuedRunInfo>();
        var reservedAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in candidateRuns)
        {
            if (selected.Count >= Math.Max(1, maxSubagents))
            {
                break;
            }

            var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId);
            if (issue is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(issue.Area) && reservedAreas.Contains(issue.Area))
            {
                continue;
            }

            selected.Add(new QueuedRunInfo
            {
                RunId = run.Id,
                IssueId = run.IssueId,
                Title = issue.Title,
                RoleSlug = run.RoleSlug,
                Area = issue.Area,
                ModelName = run.ModelName
            });

            if (!string.IsNullOrWhiteSpace(issue.Area))
            {
                reservedAreas.Add(issue.Area);
            }
        }

        return selected;
    }
}
