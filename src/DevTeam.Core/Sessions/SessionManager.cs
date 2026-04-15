using System.Security.Cryptography;
using System.Text;

namespace DevTeam.Core;

public sealed class SessionManager : ISessionManager
{
    private readonly ISystemClock _clock;

    public SessionManager(ISystemClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
    }

    public AgentSession GetOrCreateAgentSession(WorkspaceState state, int runId)
    {
        var run = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId)
            ?? throw new InvalidOperationException($"Issue #{run.IssueId} was not found.");
        var scope = DescribeSessionScope(issue);
        var binding = state.AgentSessions.FirstOrDefault(item => string.Equals(item.ScopeKey, scope.ScopeKey, StringComparison.OrdinalIgnoreCase));
        if (binding is null)
        {
            binding = new AgentSession
            {
                ScopeKey = scope.ScopeKey,
                ScopeKind = scope.ScopeKind,
                RoleSlug = issue.RoleSlug,
                IssueId = scope.IssueId,
                PipelineId = scope.PipelineId,
                SessionId = BuildSessionId(state.RepoRoot, scope.ScopeKey, issue.RoleSlug),
                UpdatedAtUtc = _clock.UtcNow
            };
            state.AgentSessions.Add(binding);
        }

        binding.RoleSlug = issue.RoleSlug;
        binding.IssueId = scope.IssueId;
        binding.PipelineId = scope.PipelineId;
        binding.LastRunId = runId;
        binding.UpdatedAtUtc = _clock.UtcNow;
        return binding;
    }

    public AgentSession GetOrCreateExecutionOrchestratorSession(WorkspaceState state)
    {
        var binding = state.AgentSessions.FirstOrDefault(item => string.Equals(item.ScopeKey, "workspace:execution-orchestrator", StringComparison.OrdinalIgnoreCase));
        if (binding is null)
        {
            binding = new AgentSession
            {
                ScopeKey = "workspace:execution-orchestrator",
                ScopeKind = "workspace",
                RoleSlug = "orchestrator",
                SessionId = BuildSessionId(state.RepoRoot, "workspace:execution-orchestrator", "orchestrator"),
                UpdatedAtUtc = _clock.UtcNow
            };
            state.AgentSessions.Add(binding);
        }

        binding.UpdatedAtUtc = _clock.UtcNow;
        return binding;
    }

    private static (string ScopeKey, string ScopeKind, int? IssueId, int? PipelineId) DescribeSessionScope(IssueItem issue)
    {
        if (issue.IsPlanningIssue
            || string.Equals(issue.RoleSlug, "planner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(issue.RoleSlug, "orchestrator", StringComparison.OrdinalIgnoreCase))
        {
            return ("workspace:planning", "workspace", issue.Id, null);
        }

        if (issue.PipelineId is not null)
        {
            return ($"pipeline:{issue.PipelineId.Value}:role:{issue.RoleSlug}", "pipeline-role", null, issue.PipelineId);
        }

        return ($"issue:{issue.Id}:role:{issue.RoleSlug}", "issue", issue.Id, null);
    }

    private static string BuildSessionId(string repoRoot, string scopeKey, string roleSlug)
    {
        var normalizedRole = NormalizeArea(roleSlug);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path.GetFullPath(repoRoot)}|{scopeKey}")))
            .ToLowerInvariant()[..12];
        return $"devteam-{normalizedRole}-{hash}";
    }

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
}
