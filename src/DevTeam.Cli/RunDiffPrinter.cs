using DevTeam.Core;
using Spectre.Console;

namespace DevTeam.Cli;

internal static class RunDiffPrinter
{
    public static string BuildMarkup(RunDiffReport report)
    {
        return report.CompareRun is null
            ? BuildSingleRunMarkup(report)
            : BuildComparisonMarkup(report);
    }

    private static string BuildSingleRunMarkup(RunDiffReport report)
    {
        var run = report.PrimaryRun;
        var issue = report.PrimaryIssue;
        var issueLabel = issue is null
            ? $"#{run.IssueId}"
            : $"#{issue.Id} [{issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $" @ {issue.Area}")}] {Markup.Escape(issue.Title)}";

        return $"""
        [bold]Run #{run.Id} diff[/]
        Issue: {issueLabel}
        Outcome: [cyan]{Markup.Escape(run.Status.ToString())}[/]
        Resulting issue status: [cyan]{Markup.Escape((run.ResultingIssueStatus?.ToString() ?? "(none)").ToLowerInvariant())}[/]

        [bold]Summary[/]
        {Markup.Escape(string.IsNullOrWhiteSpace(run.Summary) ? "(none)" : run.Summary)}

        [bold]Changed files[/]
        {BuildPathList(report.PrimaryOnlyChangedPaths)}

        [bold]Issues created[/]
        {BuildIssueList(report.PrimaryCreatedIssues)}

        [bold]Questions raised[/]
        {BuildQuestionList(report.PrimaryCreatedQuestions)}
        """;
    }

    private static string BuildComparisonMarkup(RunDiffReport report)
    {
        var compare = report.CompareRun!;
        return $"""
        [bold]Run #{report.PrimaryRun.Id} vs run #{compare.Id}[/]

        [bold]Shared changed files[/]
        {BuildPathList(report.SharedChangedPaths)}

        [bold]Only in run #{report.PrimaryRun.Id}[/]
        {BuildPathList(report.PrimaryOnlyChangedPaths)}

        [bold]Only in run #{compare.Id}[/]
        {BuildPathList(report.CompareOnlyChangedPaths)}

        [bold]Issues created by run #{report.PrimaryRun.Id}[/]
        {BuildIssueList(report.PrimaryCreatedIssues)}

        [bold]Issues created by run #{compare.Id}[/]
        {BuildIssueList(report.CompareCreatedIssues)}

        [bold]Questions raised by run #{report.PrimaryRun.Id}[/]
        {BuildQuestionList(report.PrimaryCreatedQuestions)}

        [bold]Questions raised by run #{compare.Id}[/]
        {BuildQuestionList(report.CompareCreatedQuestions)}
        """;
    }

    private static string BuildPathList(IReadOnlyList<string> paths) =>
        paths.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, paths.Select(path => $"- {Markup.Escape(path)}"));

    private static string BuildIssueList(IReadOnlyList<IssueItem> issues) =>
        issues.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, issues.Select(issue =>
                $"- #{issue.Id} [{Markup.Escape(issue.RoleSlug)}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $" @ {Markup.Escape(issue.Area)}")}] {Markup.Escape(issue.Title)}"));

    private static string BuildQuestionList(IReadOnlyList<QuestionItem> questions) =>
        questions.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, questions.Select(question =>
                $"- #{question.Id} [{(question.IsBlocking ? "blocking" : "non-blocking")}] {Markup.Escape(question.Text)}"));
}
