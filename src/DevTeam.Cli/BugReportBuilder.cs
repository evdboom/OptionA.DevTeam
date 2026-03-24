using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DevTeam.Core;

namespace DevTeam.Cli;

internal static class BugReportBuilder
{
    public static string Build(
        WorkspaceStore store,
        DevTeamRuntime runtime,
        ShellSessionDiagnostics? shellDiagnostics,
        bool redactPaths,
        int historyCount,
        int errorCount)
    {
        var replacements = CreatePathReplacements(store);
        var currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        var shellCommands = shellDiagnostics?.GetRecentCommands(historyCount) ?? [];
        var shellErrors = shellDiagnostics?.GetRecentErrors(errorCount) ?? [];
        var workspaceExists = Directory.Exists(store.WorkspacePath);
        var stateFileExists = File.Exists(store.StatePath);

        WorkspaceState? state = null;
        string? workspaceLoadError = null;
        if (stateFileExists)
        {
            try
            {
                state = store.Load();
                if (!string.IsNullOrWhiteSpace(state.RepoRoot))
                {
                    AddReplacement(replacements, state.RepoRoot, "<repo>");
                }
            }
            catch (Exception ex)
            {
                workspaceLoadError = ex.Message;
            }
        }

        var report = state is null ? null : runtime.BuildStatusReport(state);
        var version = GetToolVersion();
        var sb = new StringBuilder();
        sb.AppendLine("# DevTeam bug report draft");
        sb.AppendLine();
        sb.AppendLine("Fill in the summary and reproduction details, then paste this into a GitHub issue.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine("- Expected:");
        sb.AppendLine("- Actual:");
        sb.AppendLine("- Repro steps:");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine($"- DevTeam version: {Sanitize(version, redactPaths, replacements)}");
        sb.AppendLine($"- Timestamp (UTC): {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"- OS: {RuntimeInformation.OSDescription.Trim()}");
        sb.AppendLine($"- .NET runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"- Process architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"- Current directory: {Sanitize(currentDirectory, redactPaths, replacements)}");
        sb.AppendLine($"- Workspace path: {Sanitize(store.WorkspacePath, redactPaths, replacements)}");
        sb.AppendLine();
        sb.AppendLine("## Workspace snapshot");
        sb.AppendLine($"- Workspace directory exists: {(workspaceExists ? "yes" : "no")}");
        sb.AppendLine($"- Workspace state file exists: {(stateFileExists ? "yes" : "no")}");
        if (state is null)
        {
            sb.AppendLine($"- Workspace load status: {(workspaceLoadError is null ? "not loaded" : "failed")}");
            if (!string.IsNullOrWhiteSpace(workspaceLoadError))
            {
                sb.AppendLine($"- Workspace load error: {Sanitize(workspaceLoadError, redactPaths, replacements)}");
            }
        }
        else
        {
            sb.AppendLine("- Workspace load status: loaded");
            sb.AppendLine($"- Phase: {state.Phase}");
            sb.AppendLine($"- Active mode: {state.Runtime.ActiveModeSlug}");
            sb.AppendLine($"- Active goal: {Sanitize(state.ActiveGoal?.GoalText ?? "(none)", redactPaths, replacements)}");
            sb.AppendLine($"- Keep-awake: {(state.Runtime.KeepAwakeEnabled ? "enabled" : "disabled")}");
            sb.AppendLine($"- Auto-approve: {(state.Runtime.AutoApproveEnabled ? "enabled" : "disabled")}");
            sb.AppendLine($"- Workspace MCP: {(state.Runtime.WorkspaceMcpEnabled ? "enabled" : "disabled")}");
            sb.AppendLine($"- Pipeline scheduling: {(state.Runtime.PipelineSchedulingEnabled ? "enabled" : "disabled")}");
            sb.AppendLine($"- Repo root: {Sanitize(state.RepoRoot, redactPaths, replacements)}");
            sb.AppendLine($"- Open questions: {state.Questions.Count(item => item.Status == QuestionStatus.Open)}");
            sb.AppendLine($"- Queued/running runs: {state.AgentRuns.Count(item => item.Status is AgentRunStatus.Queued or AgentRunStatus.Running)}");
            if (report is not null)
            {
                sb.AppendLine(
                    $"- Counts: roadmap={report.Counts["roadmap"]}, issues={report.Counts["issues"]}, questions={report.Counts["questions"]}, runs={report.Counts["runs"]}, decisions={report.Counts["decisions"]}");
            }
        }

        AppendRecentCommands(sb, shellCommands, redactPaths, replacements);
        AppendRecentErrors(sb, shellErrors, redactPaths, replacements);
        AppendRecentRuns(sb, state, redactPaths, replacements);

        sb.AppendLine("## Notes");
        sb.AppendLine("- Attach screenshots or terminal recordings if the issue is interactive or visual.");
        sb.AppendLine("- Trim any private details before filing if you disable path redaction.");

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendRecentCommands(
        StringBuilder sb,
        IReadOnlyList<ShellSessionEntry> commands,
        bool redactPaths,
        IReadOnlyDictionary<string, string> replacements)
    {
        sb.AppendLine();
        sb.AppendLine("## Recent shell commands");
        if (commands.Count == 0)
        {
            sb.AppendLine("_No interactive shell command history was captured in this session._");
            return;
        }

        sb.AppendLine("```text");
        foreach (var command in commands)
        {
            sb.AppendLine($"[{command.TimestampUtc:O}] {Sanitize(command.Text, redactPaths, replacements)}");
        }
        sb.AppendLine("```");
    }

    private static void AppendRecentErrors(
        StringBuilder sb,
        IReadOnlyList<ShellSessionEntry> errors,
        bool redactPaths,
        IReadOnlyDictionary<string, string> replacements)
    {
        sb.AppendLine();
        sb.AppendLine("## Recent shell errors");
        if (errors.Count == 0)
        {
            sb.AppendLine("_No recent shell errors were captured in this session._");
            return;
        }

        sb.AppendLine("```text");
        foreach (var error in errors)
        {
            sb.AppendLine($"[{error.TimestampUtc:O}] {Sanitize(error.Text, redactPaths, replacements)}");
        }
        sb.AppendLine("```");
    }

    private static void AppendRecentRuns(
        StringBuilder sb,
        WorkspaceState? state,
        bool redactPaths,
        IReadOnlyDictionary<string, string> replacements)
    {
        sb.AppendLine();
        sb.AppendLine("## Recent agent runs");
        if (state is null)
        {
            sb.AppendLine("_No workspace state was loaded, so agent run history is unavailable._");
            return;
        }

        var recentRuns = state.AgentRuns
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(5)
            .ToList();
        if (recentRuns.Count == 0)
        {
            sb.AppendLine("_No agent runs recorded yet._");
            return;
        }

        foreach (var run in recentRuns)
        {
            var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId);
            var issueText = issue is null ? $"issue #{run.IssueId}" : $"issue #{run.IssueId} ({Sanitize(issue.Title, redactPaths, replacements)})";
            sb.AppendLine($"- Run #{run.Id}: {run.Status} on {issueText} via {run.RoleSlug}/{run.ModelName} at {run.UpdatedAtUtc:O}");
            if (!string.IsNullOrWhiteSpace(run.Summary))
            {
                sb.AppendLine($"  Summary: {Sanitize(run.Summary.Trim(), redactPaths, replacements)}");
            }
        }
    }

    private static string GetToolVersion()
    {
        var assembly = typeof(GoalInputResolver).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static Dictionary<string, string> CreatePathReplacements(WorkspaceStore store)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddReplacement(replacements, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user>");
        AddReplacement(replacements, Path.GetFullPath(Environment.CurrentDirectory), "<cwd>");
        AddReplacement(replacements, store.WorkspacePath, "<workspace>");
        AddReplacement(replacements, store.StatePath, "<workspace>\\workspace.json");
        AddReplacement(replacements, AppContext.BaseDirectory, "<app>");
        return replacements;
    }

    private static void AddReplacement(IDictionary<string, string> replacements, string? sourcePath, string token)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        replacements[Path.GetFullPath(sourcePath)] = token;
    }

    private static string Sanitize(string value, bool redactPaths, IReadOnlyDictionary<string, string> replacements)
    {
        if (!redactPaths || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value;
        foreach (var pair in replacements.OrderByDescending(item => item.Key.Length))
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
