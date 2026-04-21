namespace DevTeam.UnitTests.Tests;

internal static class LoopExecutorTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("SpawnIssueAsync_CompletesIssue_AndPersistsToStore", SpawnIssueAsync_CompletesIssue_AndPersistsToStore),
        new("SpawnIssueAsync_PersistsChangedPathsToArtifacts", SpawnIssueAsync_PersistsChangedPathsToArtifacts),
        new("SpawnIssueAsync_ThrowsForUnknownIssue", SpawnIssueAsync_ThrowsForUnknownIssue),
        new("SpawnIssueAsync_ReturnsOutcomeSummary", SpawnIssueAsync_ReturnsOutcomeSummary),
        new("SpawnIssueAsync_CancelledToken_IsGracefulBlocked", SpawnIssueAsync_CancelledToken_IsGracefulBlocked),
        new("QueueIssues_SkipsAlreadyDoneIssue", QueueIssues_SkipsAlreadyDoneIssue),
        new("TokenReporter_IsInvokedWithRoleAndTokens_DuringRunAsync", TokenReporter_IsInvokedWithRoleAndTokens_DuringRunAsync),
        new("TokenReporter_ReceivesAllTokens_AcrossMultipleWords", TokenReporter_ReceivesAllTokens_AcrossMultipleWords),
    ];

    private static WorkspaceState BuildStateWithIssue(WorkspaceStore store, string title, string role = "developer")
    {
        var state = store.Initialize("C:\\test-repo", 100, 20);
        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = title,
            RoleSlug = role,
            Status = ItemStatus.Open,
            Priority = 50
        };
        state.Issues.Add(issue);
        store.Save(state);
        return state;
    }

    private static async Task SpawnIssueAsync_CompletesIssue_AndPersistsToStore()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Write unit tests");
        var issueId = state.Issues[^1].Id;

        var agentOutput = "OUTCOME: completed\nSUMMARY:\nTests written and passing.";
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(agentOutput));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        await executor.SpawnIssueAsync(issueId, null, "fake", TimeSpan.FromSeconds(30), CancellationToken.None);

        var reloaded = store.Load();
        var issue = reloaded.Issues.First(i => i.Id == issueId);
        Assert.That(issue.Status == ItemStatus.Done, $"Expected issue to be Done but was {issue.Status}");
    }

    private static async Task SpawnIssueAsync_PersistsChangedPathsToArtifacts()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Implement traceability");
        var issueId = state.Issues[^1].Id;

        var git = new FakeGitRepository
        {
            PathsChangedSinceResult = ["src/Feature.cs", "tests/FeatureTests.cs"]
        };
        var agentOutput = "OUTCOME: completed\nSUMMARY:\nTraceability added.";
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(agentOutput));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, gitRepository: git, fileSystem: fs);

        await executor.SpawnIssueAsync(issueId, null, "fake", TimeSpan.FromSeconds(30), CancellationToken.None);

        var reloaded = store.Load();
        var run = reloaded.AgentRuns.Single(run => run.IssueId == issueId);
        Assert.That(run.ChangedPaths.SequenceEqual(["src/Feature.cs", "tests/FeatureTests.cs"]),
            $"Expected changed paths to persist on the run but got: {string.Join(", ", run.ChangedPaths)}");

        var runArtifact = fs.ReadAllText(Path.Combine(store.WorkspacePath, "runs", "run-001.md"));
        Assert.That(runArtifact.Contains("src/Feature.cs", StringComparison.Ordinal),
            $"Expected run artifact to mention changed file, got: {runArtifact}");

        var decisionArtifact = fs.ReadAllText(Path.Combine(store.WorkspacePath, "decisions", "decision-001.md"));
        Assert.That(decisionArtifact.Contains("tests/FeatureTests.cs", StringComparison.Ordinal),
            $"Expected decision artifact to mention changed file, got: {decisionArtifact}");
    }

    private static async Task SpawnIssueAsync_ThrowsForUnknownIssue()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        store.Initialize("C:\\test-repo", 100, 20);

        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nDone."));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var threw = false;
        try
        {
            await executor.SpawnIssueAsync(9999, null, "fake", TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.That(threw, "Expected InvalidOperationException for unknown issue ID");
    }

    private static async Task SpawnIssueAsync_ReturnsOutcomeSummary()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Implement feature X");
        var issueId = state.Issues[^1].Id;

        var agentOutput = "OUTCOME: completed\nSUMMARY:\nFeature X is done.";
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(agentOutput));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var result = await executor.SpawnIssueAsync(issueId, null, "fake", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.That(result.Contains("completed", StringComparison.OrdinalIgnoreCase), $"Expected 'completed' in result but got: {result}");
        Assert.That(result.Contains(issueId.ToString(), StringComparison.Ordinal), $"Expected issue ID in result but got: {result}");
    }

    private static async Task SpawnIssueAsync_CancelledToken_IsGracefulBlocked()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Long-running task", "developer");
        var issueId = state.Issues[^1].Id;

        // Delay long enough that the test can cancel the token while the agent call is in-flight.
        var factory = new FuncAgentClientFactory(_ => new FakeStaggeredAgentClient("OUTCOME: completed\nSUMMARY:\nDone.", TimeSpan.FromSeconds(5)));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        using var cts = new CancellationTokenSource();
        var task = executor.SpawnIssueAsync(issueId, null, "fake", TimeSpan.FromSeconds(30), cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await task;

        Assert.That(result.Contains("outcome: blocked", StringComparison.OrdinalIgnoreCase),
            $"Expected blocked outcome on user cancellation but got: {result}");
        Assert.That(result.Contains("Cancelled by user request.", StringComparison.OrdinalIgnoreCase),
            $"Expected graceful cancellation summary but got: {result}");

        var reloaded = store.Load();
        var run = reloaded.AgentRuns.Single(r => r.IssueId == issueId);
        Assert.That(run.Status == AgentRunStatus.Completed,
            $"Expected run to complete gracefully, but status was {run.Status}");
        Assert.That(string.Equals(run.Outcome, "blocked", StringComparison.OrdinalIgnoreCase),
            $"Expected run outcome 'blocked' on cancellation but was '{run.Outcome}'");
    }

    private static Task QueueIssues_SkipsAlreadyDoneIssue()
    {
        // Simulate: orchestrator used spawn_agent to complete an issue before QueueExecutionSelection is called.
        var runtime = new DevTeamRuntime();
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = store.Initialize("C:\\test-repo", 100, 20);

        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Already handled by spawn_agent",
            RoleSlug = "developer",
            Status = ItemStatus.Done,   // already completed via spawn_agent
            Priority = 50
        };
        state.Issues.Add(issue);
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issue.Id],
            Rationale = "test"
        };
        store.Save(state);

        var result = runtime.QueueExecutionSelection(state);

        Assert.That(result.QueuedRuns.Count == 0, $"Expected 0 queued runs (issue already Done) but got {result.QueuedRuns.Count}");
        Assert.That(issue.Status == ItemStatus.Done, $"Expected issue to remain Done but was {issue.Status}");
        return Task.CompletedTask;
    }

    private static async Task TokenReporter_IsInvokedWithRoleAndTokens_DuringRunAsync()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Stream some output", "developer");
        var issueId = state.Issues[^1].Id;
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issueId],
            Rationale = "test"
        };
        var agentOutput = "OUTCOME: completed\nSUMMARY:\nDone streaming.";
        var factory = new FuncAgentClientFactory(_ => new FakeStreamingAgentClient(agentOutput));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var reportedRoles = new List<string>();
        var reportedTokens = new List<string>();
        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            TokenReporter = (role, token) =>
            {
                reportedRoles.Add(role);
                reportedTokens.Add(token);
            }
        };

        await executor.RunAsync(state, options);

        Assert.That(reportedTokens.Count > 0, "Expected TokenReporter to be called at least once");
        Assert.That(reportedRoles.All(r => string.Equals(r, "developer", StringComparison.OrdinalIgnoreCase)),
            $"Expected all tokens attributed to 'developer' but got: {string.Join(", ", reportedRoles.Distinct())}");
        var combined = string.Concat(reportedTokens).Trim();
        Assert.That(combined.Contains("OUTCOME", StringComparison.Ordinal),
            $"Expected streamed tokens to contain 'OUTCOME' but combined was: {combined}");
    }

    private static async Task TokenReporter_ReceivesAllTokens_AcrossMultipleWords()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Multi-word streaming", "tester");
        var issueId = state.Issues[^1].Id;
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issueId],
            Rationale = "test"
        };
        const string agentOutput = "OUTCOME: completed\nSUMMARY:\nWord1 Word2 Word3 Word4 Word5.";
        var factory = new FuncAgentClientFactory(_ => new FakeStreamingAgentClient(agentOutput));
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var allTokens = new System.Text.StringBuilder();
        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            TokenReporter = (_, token) => allTokens.Append(token)
        };

        await executor.RunAsync(state, options);

        var combined = allTokens.ToString().Trim();
        // Each word in the output becomes a separate token via FakeStreamingAgentClient
        foreach (var word in new[] { "OUTCOME", "completed", "Word1", "Word5" })
        {
            Assert.That(combined.Contains(word, StringComparison.Ordinal),
                $"Expected streamed output to contain '{word}' but combined was: {combined}");
        }
    }
}
