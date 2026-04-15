namespace DevTeam.UnitTests.Tests;

internal static class SessionManagerTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("GetOrCreateSession_CreatesNew_WhenNotExists", GetOrCreateSession_CreatesNew_WhenNotExists),
        new("GetOrCreateSession_ReturnsExisting_WhenSameKey", GetOrCreateSession_ReturnsExisting_WhenSameKey),
        new("GetOrCreateSession_AssignsUniqueSessionId", GetOrCreateSession_AssignsUniqueSessionId),
    ];

    private static WorkspaceState BuildStateWithRunAndIssue(int issueId, int runId, string roleSlug = "developer")
    {
        var state = new WorkspaceState { RepoRoot = "C:\\test-repo" };
        state.Issues.Add(new IssueItem
        {
            Id = issueId,
            Title = $"Issue {issueId}",
            RoleSlug = roleSlug,
            IsPlanningIssue = false
        });
        state.AgentRuns.Add(new AgentRun
        {
            Id = runId,
            IssueId = issueId,
            RoleSlug = roleSlug,
            Status = AgentRunStatus.Queued
        });
        return state;
    }

    private static Task GetOrCreateSession_CreatesNew_WhenNotExists()
    {
        var svc = new SessionManager(new FakeSystemClock());
        var state = BuildStateWithRunAndIssue(issueId: 1, runId: 1);

        var session = svc.GetOrCreateAgentSession(state, runId: 1);

        Assert.That(session is not null, "Expected a session to be created");
        Assert.That(state.AgentSessions.Count == 1, $"Expected 1 session but got {state.AgentSessions.Count}");
        Assert.That(!string.IsNullOrEmpty(session!.SessionId), "Expected a non-empty SessionId");
        return Task.CompletedTask;
    }

    private static Task GetOrCreateSession_ReturnsExisting_WhenSameKey()
    {
        var svc = new SessionManager(new FakeSystemClock());
        var state = BuildStateWithRunAndIssue(issueId: 1, runId: 1);

        var first = svc.GetOrCreateAgentSession(state, runId: 1);
        var second = svc.GetOrCreateAgentSession(state, runId: 1);

        Assert.That(state.AgentSessions.Count == 1, $"Expected 1 session but got {state.AgentSessions.Count}");
        Assert.That(first.SessionId == second.SessionId,
            $"Expected same SessionId but got '{first.SessionId}' vs '{second.SessionId}'");
        return Task.CompletedTask;
    }

    private static Task GetOrCreateSession_AssignsUniqueSessionId()
    {
        var svc = new SessionManager(new FakeSystemClock());
        var state = new WorkspaceState { RepoRoot = "C:\\test-repo" };
        state.Issues.Add(new IssueItem { Id = 1, Title = "Issue A", RoleSlug = "developer", IsPlanningIssue = false });
        state.Issues.Add(new IssueItem { Id = 2, Title = "Issue B", RoleSlug = "tester", IsPlanningIssue = false });
        state.AgentRuns.Add(new AgentRun { Id = 1, IssueId = 1, RoleSlug = "developer", Status = AgentRunStatus.Queued });
        state.AgentRuns.Add(new AgentRun { Id = 2, IssueId = 2, RoleSlug = "tester", Status = AgentRunStatus.Queued });

        var sessionA = svc.GetOrCreateAgentSession(state, runId: 1);
        var sessionB = svc.GetOrCreateAgentSession(state, runId: 2);

        Assert.That(sessionA.SessionId != sessionB.SessionId,
            $"Expected unique SessionIds but both were '{sessionA.SessionId}'");
        return Task.CompletedTask;
    }
}
