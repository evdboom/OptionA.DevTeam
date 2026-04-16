using System.Text;
using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli;

internal static class OnboardingGuideBuilder
{
    internal static string BuildMarkup(WorkspaceState? state, DevTeamRuntime runtime, string? personaInput)
    {
        var persona = NormalizePersona(personaInput);
        var readyIssueCount = state is not null && state.Phase == WorkflowPhase.Execution
            ? runtime.GetReadyIssuesPreview(state, Math.Max(1, state.Runtime.DefaultMaxSubagents)).Count
            : 0;

        var sb = new StringBuilder();
        sb.AppendLine($"[bold]Start here — {Markup.Escape(GetPersonaTitle(persona))}[/]");
        sb.AppendLine($"[dim]{Markup.Escape(GetPersonaSummary(persona))}[/]");
        sb.AppendLine();
        sb.AppendLine($"[bold]Next step:[/] {BuildNextStepMarkup(state, readyIssueCount)}");
        sb.AppendLine();
        sb.AppendLine("[bold]Recommended flow:[/]");
        foreach (var step in BuildPersonaSteps(persona))
        {
            sb.AppendLine($"  {step}");
        }
        sb.AppendLine();
        sb.AppendLine($"[bold]Good defaults:[/] {BuildDefaultsMarkup(persona)}");
        return sb.ToString().TrimEnd();
    }

    private static string NormalizePersona(string? personaInput) =>
        personaInput?.Trim().ToLowerInvariant() switch
        {
            null or "" => "new",
            "new" or "beginner" or "non-programmer" or "nonprogrammer" => "new",
            "medior" or "intermediate" => "medior",
            "expert" or "advanced" => "expert",
            _ => throw new InvalidOperationException("Unknown persona. Use `new`, `medior`, or `expert`.")
        };

    private static string GetPersonaTitle(string persona) =>
        persona switch
        {
            "new" => "new user / non-programmer",
            "medior" => "medior user",
            "expert" => "expert user",
            _ => persona
        };

    private static string GetPersonaSummary(string persona) =>
        persona switch
        {
            "new" => "Plain-English workflow with safe defaults and explicit next steps.",
            "medior" => "A balanced workflow with visibility into issues, questions, and budget.",
            "expert" => "Fast path for customization, parallelism, and higher autonomy.",
            _ => ""
        };

    private static string BuildNextStepMarkup(WorkspaceState? state, int readyIssueCount)
    {
        if (state is null)
        {
            return "Create a workspace with [cyan]/init[/] and describe your goal in plain English.";
        }

        var openQuestions = state.Questions.Where(question => question.Status == QuestionStatus.Open).ToList();
        if (openQuestions.Count > 0)
        {
            return openQuestions.Count == 1
                ? "Answer the open question with plain text or [cyan]/answer[/]."
                : $"Use [cyan]/questions[/] and [cyan]/answer[/] to clear {openQuestions.Count} open questions.";
        }

        if (state.Phase == WorkflowPhase.Planning && !state.Issues.Any(issue => issue.IsPlanningIssue && issue.Status == ItemStatus.Done))
        {
            return "Run [cyan]/plan[/] to generate the first high-level plan.";
        }

        if (state.Phase == WorkflowPhase.Planning)
        {
            return "Review the plan, type feedback as plain text, or use [cyan]/approve[/].";
        }

        if (state.Phase == WorkflowPhase.ArchitectPlanning && !PlanWorkflow.IsAwaitingArchitectApproval(state))
        {
            return "Run [cyan]/run[/] to let the architect turn the plan into execution issues.";
        }

        if (PlanWorkflow.IsAwaitingArchitectApproval(state))
        {
            return "Review the architect plan, then type feedback or use [cyan]/approve[/].";
        }

        if (readyIssueCount > 0)
        {
            return "Preview the next batch with [cyan]/preview[/], then start it with [cyan]/run[/].";
        }

        return "Use [cyan]/status[/] to inspect the board or [cyan]/add-issue[/] to queue work manually.";
    }

    private static IReadOnlyList<string> BuildPersonaSteps(string persona) =>
        persona switch
        {
            "new" =>
            [
                "1. [cyan]/init[/] --goal \"Describe the outcome you want in plain English.\"",
                "2. [cyan]/plan[/] to generate the first high-level plan.",
                "3. Type feedback as plain text or use [cyan]/approve[/] when the plan looks good.",
                "4. Set [cyan]/max-subagents 1[/] while you are still learning the loop.",
                "5. Use [cyan]/preview[/] before your first execution run.",
                "6. Start small with [cyan]/run[/] --max-iterations 3."
            ],
            "medior" =>
            [
                "1. [cyan]/init[/] and [cyan]/plan[/] to get the first workflow moving.",
                "2. Review with plain-text feedback or [cyan]/approve[/] at each approval gate.",
                "3. Use [cyan]/status[/], [cyan]/questions[/], and [cyan]/budget[/] while the loop runs.",
                "4. Check [cyan]/preview[/] before increasing concurrency.",
                "5. Run with [cyan]/max-subagents 2[/] or [cyan]3[/] once the batch looks right."
            ],
            "expert" =>
            [
                "1. [cyan]/init[/], then optionally [cyan]/customize[/] to edit roles and superpowers.",
                "2. Review the default flow with [cyan]/plan[/] and [cyan]/preview[/] before turning autonomy up.",
                "3. Enable [cyan]/worktrees on[/] when you want safer parallel isolation.",
                "4. Use [cyan]/run[/] with higher concurrency once you trust the current batch selection.",
                "5. Reach for [cyan]@role[/], [cyan]/mode autopilot[/], and deeper customization after you understand recovery paths."
            ],
            _ => []
        };

    private static string BuildDefaultsMarkup(string persona) =>
        persona switch
        {
            "new" => "Stay sequential with [cyan]/max-subagents 1[/], use [cyan]/preview[/], and talk to DevTeam in plain English.",
            "medior" => "Keep [cyan]/status[/] and [cyan]/questions[/] close, and treat [cyan]/preview[/] as the checkpoint before each run.",
            "expert" => "Use [cyan]/customize[/], [cyan]/worktrees on[/], and [cyan]/mode autopilot[/] deliberately rather than by default.",
            _ => ""
        };
}
