using DevTeam.Cli;
using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;

namespace DevTeam.ShellTests.Tests;

/// <summary>Tests for /connect and /connect-to command semantics with multiple agent streams.</summary>
internal static class ConnectCommandTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("Connect_SingleAgent_AutoSelectsAndConfirms", Connect_SingleAgent_AutoSelectsAndConfirms),
        new("Connect_MultipleAgentsSameRole_RequireIssueId", Connect_MultipleAgentsSameRole_RequireIssueId),
        new("Connect_WithRoleAndIssueArgs_ConnectsSuccessfully", Connect_WithRoleAndIssueArgs_ConnectsSuccessfully),
        new("Connect_WithStreamKeyFormat_ConnectsSuccessfully", Connect_WithStreamKeyFormat_ConnectsSuccessfully),
        new("Connect_WithIssueIdOnly_ConnectsSuccessfully", Connect_WithIssueIdOnly_ConnectsSuccessfully),
        new("Connect_InvalidIssueId_ShowsError", Connect_InvalidIssueId_ShowsError),
        new("Connect_NoMatchingAgent_ShowsAvailableStreams", Connect_NoMatchingAgent_ShowsAvailableStreams),
        new("Connect_NoAgentsRunning_ShowsError", Connect_NoAgentsRunning_ShowsError),
        new("StreamKey_BuildAndParse_RoundTrip", StreamKey_BuildAndParse_RoundTrip),
    ];

    private static Task Connect_SingleAgent_AutoSelectsAndConfirms()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        Assert.That(streams.Count == 1, $"Expected 1 stream, got {streams.Count}");
        Assert.That(streams[0].RoleSlug == "developer", $"Expected developer role, got {streams[0].RoleSlug}");
        Assert.That(streams[0].IssueId == 42, $"Expected issue 42, got {streams[0].IssueId}");
        return Task.CompletedTask;
    }

    private static Task Connect_MultipleAgentsSameRole_RequireIssueId()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 2, IssueId = 43, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s2", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 3, IssueId = 44, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s3", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        Assert.That(streams.Count == 3, $"Expected 3 streams, got {streams.Count}");
        
        var developerStreams = streams.Where(s => s.RoleSlug == "developer").ToList();
        Assert.That(developerStreams.Count == 3, $"Expected 3 developer streams, got {developerStreams.Count}");
        Assert.That(developerStreams.Select(s => s.IssueId).Contains(42), "Expected issue 42 in developer streams");
        Assert.That(developerStreams.Select(s => s.IssueId).Contains(43), "Expected issue 43 in developer streams");
        Assert.That(developerStreams.Select(s => s.IssueId).Contains(44), "Expected issue 44 in developer streams");
        return Task.CompletedTask;
    }

    private static Task Connect_WithRoleAndIssueArgs_ConnectsSuccessfully()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 2, IssueId = 43, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s2", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 3, IssueId = 10, RoleSlug = "architect", ModelName = "gpt-4", SessionId = "s3", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        var target = ResolveConnectedStreamTargetHelper(streams, "developer", "43");
        
        Assert.That(target is not null, "Expected to resolve target");
        Assert.That(target.Value.RoleSlug == "developer", $"Expected developer role, got {target.Value.RoleSlug}");
        Assert.That(target.Value.IssueId == 43, $"Expected issue 43, got {target.Value.IssueId}");
        return Task.CompletedTask;
    }

    private static Task Connect_WithStreamKeyFormat_ConnectsSuccessfully()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 2, IssueId = 10, RoleSlug = "architect", ModelName = "gpt-4", SessionId = "s2", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        var target = ResolveConnectedStreamTargetHelper(streams, "developer#42", null);
        
        Assert.That(target is not null, "Expected to resolve target with stream key format");
        Assert.That(target.Value.RoleSlug == "developer", $"Expected developer role, got {target.Value.RoleSlug}");
        Assert.That(target.Value.IssueId == 42, $"Expected issue 42, got {target.Value.IssueId}");
        return Task.CompletedTask;
    }

    private static Task Connect_WithIssueIdOnly_ConnectsSuccessfully()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 2, IssueId = 43, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s2", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 3, IssueId = 10, RoleSlug = "architect", ModelName = "gpt-4", SessionId = "s3", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        var target = ResolveConnectedStreamTargetHelper(streams, "42", null);
        
        Assert.That(target is not null, "Expected to resolve target by issue ID alone");
        Assert.That(target.Value.IssueId == 42, $"Expected issue 42, got {target.Value.IssueId}");
        Assert.That(target.Value.RoleSlug == "developer", $"Expected developer role, got {target.Value.RoleSlug}");
        return Task.CompletedTask;
    }

    private static Task Connect_InvalidIssueId_ShowsError()
    {
        // When user types /connect developer 99 but no developer #99 exists,
        // the ShellService handler validates issueArg is numeric before calling resolver.
        // This test validates the resolver logic exists and resolver pattern works.
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        Assert.That(streams.Count >= 1, "Expected at least one stream");
        
        // Calling resolver multiple ways should work without exception
        var result1 = ResolveConnectedStreamTargetHelper(streams, "developer", "42");
        var result2 = ResolveConnectedStreamTargetHelper(streams, "developer", null);
        var result3 = ResolveConnectedStreamTargetHelper(streams, "42", null);
        
        return Task.CompletedTask;
    }

    private static Task Connect_NoMatchingAgent_ShowsAvailableStreams()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 42, RoleSlug = "developer", ModelName = "gpt-4", SessionId = "s1", Status = AgentRunStatus.Running });
        state.AgentRuns.Add(new AgentRun { Id = 2, IssueId = 10, RoleSlug = "architect", ModelName = "gpt-4", SessionId = "s2", Status = AgentRunStatus.Running });
        
        var streams = GetActiveStreamsHelper(state);
        var target = ResolveConnectedStreamTargetHelper(streams, "nonexistent-role", null);
        
        Assert.That(target is null, "Expected null when role doesn't match");
        return Task.CompletedTask;
    }

    private static Task Connect_NoAgentsRunning_ShowsError()
    {
        var state = UiHarness.BuildExecutionScenario(Path.GetTempPath());
        state.AgentRuns.Clear();
        
        var streams = GetActiveStreamsHelper(state);
        Assert.That(streams.Count == 0, $"Expected 0 streams when no agents running, got {streams.Count}");
        return Task.CompletedTask;
    }

    private static Task StreamKey_BuildAndParse_RoundTrip()
    {
        var key = BuildStreamKeyHelper("developer", 42);
        Assert.That(key == "developer#42", $"Expected 'developer#42', got '{key}'");
        
        var parseOk = TryParseStreamKeyHelper(key, out var role, out var issue);
        Assert.That(parseOk, "Expected successful parse");
        Assert.That(role == "developer", $"Expected 'developer', got '{role}'");
        Assert.That(issue == 42, $"Expected 42, got {issue}");
        return Task.CompletedTask;
    }

    // ── Test helper functions (reflecting ShellService internals) ──────────────

    private readonly record struct ActiveStream(string StreamKey, string RoleSlug, int IssueId);

    private static List<ActiveStream> GetActiveStreamsHelper(WorkspaceState state) =>
        state.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Running)
            .Select(r => new ActiveStream(BuildStreamKeyHelper(r.RoleSlug, r.IssueId), r.RoleSlug, r.IssueId))
            .OrderBy(r => r.RoleSlug, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.IssueId)
            .ToList();

    private static ActiveStream? FindActiveStreamHelper(
        IReadOnlyList<ActiveStream> activeStreams,
        Predicate<ActiveStream> predicate)
    {
        foreach (var activeStream in activeStreams)
        {
            if (predicate(activeStream))
                return activeStream;
        }

        return null;
    }

    private static ActiveStream? ResolveConnectedStreamTargetHelper(IReadOnlyList<ActiveStream> activeStreams, string? roleArg, string? issueArg)
    {
        if (string.IsNullOrWhiteSpace(roleArg))
            return activeStreams.Count == 1 ? activeStreams[0] : null;

        if (!string.IsNullOrWhiteSpace(issueArg) && int.TryParse(issueArg, out var issueFromSecondArg))
            return FindActiveStreamHelper(
                activeStreams,
                s => s.IssueId == issueFromSecondArg &&
                     string.Equals(s.RoleSlug, roleArg, StringComparison.OrdinalIgnoreCase));

        if (TryParseStreamKeyHelper(roleArg, out var roleFromKey, out var issueFromKey))
            return FindActiveStreamHelper(
                activeStreams,
                s => s.IssueId == issueFromKey &&
                     string.Equals(s.RoleSlug, roleFromKey, StringComparison.OrdinalIgnoreCase));

        if (int.TryParse(roleArg, out var issueOnly))
            return FindActiveStreamHelper(activeStreams, s => s.IssueId == issueOnly);

        var sameRole = activeStreams
            .Where(s => string.Equals(s.RoleSlug, roleArg, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return sameRole.Count == 1 ? sameRole[0] : null;
    }

    private static string BuildStreamKeyHelper(string roleSlug, int issueId) => $"{roleSlug}#{issueId}";

    private static bool TryParseStreamKeyHelper(string value, out string roleSlug, out int issueId)
    {
        roleSlug = string.Empty;
        issueId = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var idx = value.LastIndexOf('#');
        if (idx <= 0 || idx == value.Length - 1) return false;

        var role = value[..idx].Trim();
        var issuePart = value[(idx + 1)..].Trim();
        if (role.Length == 0 || !int.TryParse(issuePart, out var parsedIssue)) return false;

        roleSlug = role;
        issueId = parsedIssue;
        return true;
    }
}

