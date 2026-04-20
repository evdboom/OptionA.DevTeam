using System.Text.Json;
using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class GitHubIssueSyncService(ICommandRunner? runner = null)
{
    private const string GitHubCliPathEnvVar = "DEVTEAM_GH_PATH";
    private readonly ICommandRunner _runner = runner ?? new ProcessCommandRunner();

    public async Task<GitHubSyncReport> SyncAsync(WorkspaceState state, DevTeamRuntime runtime, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var payloads = await LoadOpenIssuesAsync(workingDirectory, cancellationToken);

        var report = new GitHubSyncReport();
        report.SkippedCount += payloads.Count(payload => !IsQuestion(payload) && !IsReadyIssue(payload));
        var importedIssues = new Dictionary<int, IssueItem>();
        foreach (var payload in payloads.Where(IsQuestion).OrderBy(item => item.Number))
        {
            SyncQuestion(state, runtime, payload, report);
        }

        foreach (var payload in payloads.Where(item => !IsQuestion(item) && IsReadyIssue(item)).OrderBy(item => item.Number))
        {
            var issue = SyncIssue(state, runtime, payload, report);
            if (issue is not null)
            {
                importedIssues[payload.Number] = issue;
            }
        }

        foreach (var payload in payloads.Where(item => !IsQuestion(item) && IsReadyIssue(item)).OrderBy(item => item.Number))
        {
            if (!importedIssues.TryGetValue(payload.Number, out var issue))
            {
                continue;
            }

            issue.DependsOnIssueIds = payload.DependsOnNumbers
                .Where(importedIssues.ContainsKey)
                .Select(number => importedIssues[number].Id)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        runtime.RecordDecision(
            state,
            "Synced GitHub work queue",
            $"Imported issues: {report.ImportedIssueCount}, updated issues: {report.UpdatedIssueCount}, imported questions: {report.ImportedQuestionCount}, updated questions: {report.UpdatedQuestionCount}, skipped: {report.SkippedCount}.",
            "github-sync");
        return report;
    }

    private async Task<List<GitHubIssuePayload>> LoadOpenIssuesAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new CommandExecutionSpec
            {
                FileName = ResolveGitHubCliPath(),
                Arguments = ["issue", "list", "--state", "open", "--limit", "100", "--json", "number,title,body,labels"],
                WorkingDirectory = workingDirectory,
                Timeout = TimeSpan.FromMinutes(2)
            },
            cancellationToken);

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr) ? "Failed to query GitHub issues." : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(result.StdOut) ? "[]" : result.StdOut);
        var payloads = new List<GitHubIssuePayload>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var labels = element.TryGetProperty("labels", out var labelsElement)
                ? labelsElement.EnumerateArray()
                    .Select(label => label.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "")
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList()
                : [];
            payloads.Add(ParsePayload(
                element.GetProperty("number").GetInt32(),
                element.GetProperty("title").GetString() ?? "",
                element.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "",
                labels));
        }

        return payloads;
    }

    private static GitHubIssuePayload ParsePayload(int number, string title, string body, IReadOnlyList<string> labels)
    {
        var normalizedBody = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var metadata = ParseFrontmatter(normalizedBody, out var detail);
        var role = GetMetadataValue(metadata, "role")
            ?? GetLabelValue(labels, "role:")
            ?? "developer";
        var priority = TryParseInt(GetMetadataValue(metadata, "priority"))
            ?? TryParseInt(GetLabelValue(labels, "priority:"))
            ?? 50;
        var area = GetMetadataValue(metadata, "area")
            ?? GetLabelValue(labels, "area:")
            ?? "";
        var dependsOnNumbers = ParseDependsOn(GetMetadataValue(metadata, "depends"));
        var isBlocking = ParseBool(GetMetadataValue(metadata, "blocking"))
            ?? labels.Any(label => string.Equals(label, "devteam:blocking", StringComparison.OrdinalIgnoreCase));

        return new GitHubIssuePayload(
            number,
            title.Trim(),
            string.IsNullOrWhiteSpace(detail) ? title.Trim() : detail.Trim(),
            labels,
            role.Trim(),
            Math.Clamp(priority, 1, 100),
            NormalizeArea(area),
            dependsOnNumbers,
            isBlocking);
    }

    private static Dictionary<string, string> ParseFrontmatter(string body, out string detail)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        detail = body.Trim();
        if (!body.TrimStart().StartsWith("---", StringComparison.Ordinal))
        {
            return metadata;
        }

        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 3 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            return metadata;
        }

        var endIndex = Array.FindIndex(lines, 1, line => string.Equals(line.Trim(), "---", StringComparison.Ordinal));
        if (endIndex < 0)
        {
            return metadata;
        }

        foreach (var line in lines.Skip(1).Take(endIndex - 1))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                metadata[key] = value;
            }
        }

        detail = string.Join('\n', lines.Skip(endIndex + 1)).Trim();
        return metadata;
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string? GetLabelValue(IEnumerable<string> labels, string prefix) =>
        labels.FirstOrDefault(label => label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].Trim();

    private static int? TryParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : null;

    private static bool? ParseBool(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "1" or "on" => true,
            "false" or "no" or "0" or "off" => false,
            _ => null
        };

    private static List<int> ParseDependsOn(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim().TrimStart('#'))
                .Where(token => int.TryParse(token, out _))
                .Select(int.Parse)
                .Distinct()
                .ToList();

    private static bool IsQuestion(GitHubIssuePayload payload) =>
        payload.Labels.Any(label => string.Equals(label, "devteam:question", StringComparison.OrdinalIgnoreCase));

    private static bool IsReadyIssue(GitHubIssuePayload payload) =>
        payload.Labels.Any(label => string.Equals(label, "devteam:ready", StringComparison.OrdinalIgnoreCase));

    private static string BuildExternalReference(int number) => $"github#{number}";

    private static string ResolveGitHubCliPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(GitHubCliPathEnvVar);
        return string.IsNullOrWhiteSpace(configuredPath) ? "gh" : configuredPath.Trim();
    }

    private static string BuildQuestionText(GitHubIssuePayload payload) =>
        string.IsNullOrWhiteSpace(payload.Detail) || string.Equals(payload.Detail, payload.Title, StringComparison.Ordinal)
            ? payload.Title
            : $"{payload.Title}\n\n{payload.Detail}";

    private static string NormalizeArea(string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return "";
        }

        var chars = area.Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    private static void SyncQuestion(WorkspaceState state, DevTeamRuntime runtime, GitHubIssuePayload payload, GitHubSyncReport report)
    {
        var externalReference = BuildExternalReference(payload.Number);
        var existing = state.Questions.FirstOrDefault(item => string.Equals(item.ExternalReference, externalReference, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Status != QuestionStatus.Open)
            {
                report.SkippedCount++;
                return;
            }

            existing.Text = BuildQuestionText(payload);
            existing.IsBlocking = payload.IsBlocking;
            report.UpdatedQuestionCount++;
            return;
        }

        var created = runtime.AddQuestion(state, BuildQuestionText(payload), payload.IsBlocking);
        created.ExternalReference = externalReference;
        report.ImportedQuestionCount++;
    }

    private IssueItem? SyncIssue(WorkspaceState state, DevTeamRuntime runtime, GitHubIssuePayload payload, GitHubSyncReport report)
    {
        var externalReference = BuildExternalReference(payload.Number);
        var existing = state.Issues.FirstOrDefault(item => string.Equals(item.ExternalReference, externalReference, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing.Status == ItemStatus.Done)
            {
                report.SkippedCount++;
                return null;
            }

            if (state.AgentRuns.Any(run => run.IssueId == existing.Id))
            {
                existing.Title = payload.Title;
                existing.Detail = payload.Detail;
                existing.Priority = payload.Priority;
                if (!string.IsNullOrWhiteSpace(payload.Area))
                {
                    existing.Area = payload.Area;
                }
                report.UpdatedIssueCount++;
                return existing;
            }

            existing.Title = payload.Title;
            existing.Detail = payload.Detail;
            existing.Priority = payload.Priority;
            existing.Area = payload.Area;
            existing.RoleSlug = runtime.TryResolveRoleSlug(state, payload.RoleSlug, out var resolvedRole) ? resolvedRole : payload.RoleSlug;
            report.UpdatedIssueCount++;
            return existing;
        }

        var created = runtime.AddIssue(
            state,
            new IssueRequest
            {
                Title = payload.Title,
                Detail = payload.Detail,
                RoleSlug = payload.RoleSlug,
                Priority = payload.Priority,
                RoadmapItemId = null,
                DependsOn = [],
                Area = payload.Area,
                FamilyKey = null,
                ParentIssueId = null,
                PipelineId = null,
                PipelineStageIndex = null,
                ComplexityHint = null
            });
        created.ExternalReference = externalReference;
        report.ImportedIssueCount++;
        return created;
    }
}

internal sealed record GitHubSyncReport
{
    public int ImportedIssueCount { get; set; }
    public int UpdatedIssueCount { get; set; }
    public int ImportedQuestionCount { get; set; }
    public int UpdatedQuestionCount { get; set; }
    public int SkippedCount { get; set; }
}

internal sealed record GitHubIssuePayload(
    int Number,
    string Title,
    string Detail,
    IReadOnlyList<string> Labels,
    string RoleSlug,
    int Priority,
    string Area,
    IReadOnlyList<int> DependsOnNumbers,
    bool IsBlocking);
