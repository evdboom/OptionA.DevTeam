using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorkspaceStore(string workspacePath)
    {
        WorkspacePath = Path.GetFullPath(workspacePath);
        StatePath = Path.Combine(WorkspacePath, "workspace.json");
    }

    public string WorkspacePath { get; }
    public string StatePath { get; }

    public WorkspaceState Initialize(string repoRoot, double totalCreditCap, double premiumCreditCap)
    {
        ResetWorkspaceArtifacts();
        Directory.CreateDirectory(WorkspacePath);
        Directory.CreateDirectory(Path.Combine(WorkspacePath, "runs"));
        Directory.CreateDirectory(Path.Combine(WorkspacePath, "decisions"));
        Directory.CreateDirectory(Path.Combine(WorkspacePath, "artifacts"));
        var state = SeedData.BuildInitialState(repoRoot, totalCreditCap, premiumCreditCap);
        Save(state);
        return state;
    }

    public WorkspaceState Load()
    {
        if (!File.Exists(StatePath))
        {
            throw new InvalidOperationException(
                $"Workspace state not found at '{StatePath}'. Run 'init' first.");
        }

        var json = File.ReadAllText(StatePath);
        var state = JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions);
        if (state is null)
        {
            throw new InvalidOperationException("Failed to deserialize workspace state.");
        }

        if (SeedData.HydrateMissingWorkspaceMetadata(state))
        {
            Save(state);
        }

        return state;
    }

    public void Save(WorkspaceState state)
    {
        Directory.CreateDirectory(WorkspacePath);
        Directory.CreateDirectory(Path.Combine(WorkspacePath, "runs"));
        Directory.CreateDirectory(Path.Combine(WorkspacePath, "decisions"));
        Directory.CreateDirectory(Path.Combine(WorkspacePath, "artifacts"));
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
        WriteIssueBoard(state);
        WriteQuestionsFile(state);
    }

    private void ResetWorkspaceArtifacts()
    {
        if (!Directory.Exists(WorkspacePath))
        {
            return;
        }

        foreach (var directory in new[] { "runs", "decisions", "artifacts", "issues" })
        {
            var path = Path.Combine(WorkspacePath, directory);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        foreach (var file in new[] { "workspace.json", "questions.md", "plan.md" })
        {
            var path = Path.Combine(WorkspacePath, file);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private void WriteQuestionsFile(WorkspaceState state)
    {
        var openQuestions = state.Questions
            .Where(item => item.Status == QuestionStatus.Open)
            .OrderBy(item => item.Id)
            .ToList();
        var path = Path.Combine(WorkspacePath, "questions.md");

        if (openQuestions.Count == 0)
        {
            File.WriteAllText(path, "# Open questions\n\n(none)\n");
            return;
        }

        var lines = new List<string>
        {
            "# Open questions",
            ""
        };

        foreach (var question in openQuestions)
        {
            lines.Add($"## Question {question.Id}");
            lines.Add("");
            lines.Add($"- Type: {(question.IsBlocking ? "blocking" : "non-blocking")}");
            lines.Add($"- Status: {question.Status}");
            lines.Add("");
            lines.Add(question.Text);
            lines.Add("");
        }

        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private void WriteIssueBoard(WorkspaceState state)
    {
        var issuesDir = Path.Combine(WorkspacePath, "issues");
        Directory.CreateDirectory(issuesDir);

        var indexLines = new List<string>
        {
            "# Issue Index",
            "",
            "| ID   | Title | Status | Role | Area | Depends On |",
            "|------|-------|--------|------|------|------------|"
        };

        foreach (var issue in state.Issues.OrderBy(item => item.Id))
        {
            var dependsOn = issue.DependsOnIssueIds.Count == 0
                ? "—"
                : string.Join(", ", issue.DependsOnIssueIds.Select(FormatIssueId));
            indexLines.Add(
                $"| {FormatIssueId(issue.Id)} | {EscapePipe(issue.Title)} | {issue.Status.ToString().ToLowerInvariant()} | {issue.RoleSlug} | {(string.IsNullOrWhiteSpace(issue.Area) ? "—" : issue.Area)} | {dependsOn} |");

            WriteIssueFile(issuesDir, state, issue);
        }

        File.WriteAllText(
            Path.Combine(issuesDir, "_index.md"),
            string.Join(Environment.NewLine, indexLines) + Environment.NewLine);
    }

    private static void WriteIssueFile(string issuesDir, WorkspaceState state, IssueItem issue)
    {
        var latestRun = state.AgentRuns
            .Where(run => run.IssueId == issue.Id)
            .OrderByDescending(run => run.UpdatedAtUtc)
            .FirstOrDefault();
        var relatedDecisions = state.Decisions
            .Where(item => item.IssueId == issue.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(5)
            .ToList();

        var path = Path.Combine(issuesDir, $"{FormatIssueId(issue.Id)}-{Slugify(issue.Title)}.md");
        var dependsOn = issue.DependsOnIssueIds.Count == 0
            ? "none"
            : string.Join(", ", issue.DependsOnIssueIds.Select(FormatIssueId));
        var roadmap = issue.RoadmapItemId is null ? "none" : issue.RoadmapItemId.Value.ToString();
        var latestRunBlock = latestRun is null
            ? "(none)"
            : $"""
            - Run: {latestRun.Id}
            - Status: {latestRun.Status}
            - Model: {latestRun.ModelName}
            - Updated: {latestRun.UpdatedAtUtc:O}
            - Summary: {latestRun.Summary}
            - Superpowers Used: {(latestRun.SuperpowersUsed.Count == 0 ? "none" : string.Join(", ", latestRun.SuperpowersUsed))}
            - Tools Used: {(latestRun.ToolsUsed.Count == 0 ? "none" : string.Join(", ", latestRun.ToolsUsed))}
            """;
        var decisionBlock = relatedDecisions.Count == 0
            ? "(none)"
            : string.Join(
                Environment.NewLine,
                relatedDecisions.Select(item => $"- #{item.Id} [{item.Source}] {item.Title}: {item.Detail}"));

        var content = $"""
        # Issue {FormatIssueId(issue.Id)}: {issue.Title}

        - Status: {issue.Status.ToString().ToLowerInvariant()}
        - Role: {issue.RoleSlug}
        - Area: {(string.IsNullOrWhiteSpace(issue.Area) ? "none" : issue.Area)}
        - Priority: {issue.Priority}
        - Depends On: {dependsOn}
        - Roadmap Item: {roadmap}
        - Planning Issue: {(issue.IsPlanningIssue ? "yes" : "no")}

        ## Detail

        {issue.Detail}

        ## Latest Run

        {latestRunBlock}

        ## Recent Decisions

        {decisionBlock}
        """;

        File.WriteAllText(path, content);
    }

    private static string FormatIssueId(int id) => id.ToString("0000");

    private static string EscapePipe(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Slugify(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }
}

