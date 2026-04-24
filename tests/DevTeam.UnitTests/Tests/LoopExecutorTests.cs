namespace DevTeam.UnitTests.Tests;

internal static class LoopExecutorTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("SpawnIssueAsync_CompletesIssue_AndPersistsToStore", SpawnIssueAsync_CompletesIssue_AndPersistsToStore),
        new("SpawnIssueAsync_IncludesContextHintInPrompt", SpawnIssueAsync_IncludesContextHintInPrompt),
        new("SpawnIssueAsync_PersistsChangedPathsToArtifacts", SpawnIssueAsync_PersistsChangedPathsToArtifacts),
        new("SpawnIssueAsync_ThrowsForUnknownIssue", SpawnIssueAsync_ThrowsForUnknownIssue),
        new("SpawnIssueAsync_ReturnsOutcomeSummary", SpawnIssueAsync_ReturnsOutcomeSummary),
        new("SpawnIssueAsync_CancelledToken_IsGracefulBlocked", SpawnIssueAsync_CancelledToken_IsGracefulBlocked),
        new("QueueIssues_SkipsAlreadyDoneIssue", QueueIssues_SkipsAlreadyDoneIssue),
        new("TokenReporter_IsInvokedWithRoleAndTokens_DuringRunAsync", TokenReporter_IsInvokedWithRoleAndTokens_DuringRunAsync),
        new("TokenReporter_ReceivesAllTokens_AcrossMultipleWords", TokenReporter_ReceivesAllTokens_AcrossMultipleWords),
        new("DetailedVerbosity_PassesHooksToRequest", DetailedVerbosity_PassesHooksToRequest),
        new("NormalVerbosity_DoesNotPassHooksToRequest", NormalVerbosity_DoesNotPassHooksToRequest),
        new("DetailedVerbosity_HooksLogPreAndPostToolUse", DetailedVerbosity_HooksLogPreAndPostToolUse),
        new("DetailedVerbosity_HooksAbortOnError", DetailedVerbosity_HooksAbortOnError),
        new("NormalVerbosity_OrchestratorRequest_PassesVisibilityHooks", NormalVerbosity_OrchestratorRequest_PassesVisibilityHooks),
        new("OrchestratorVisibilityHooks_LogSpawnAndInlineEvents", OrchestratorVisibilityHooks_LogSpawnAndInlineEvents),
        new("OrchestratorVisibilityHooks_AbortOnError", OrchestratorVisibilityHooks_AbortOnError),
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

    private static async Task SpawnIssueAsync_IncludesContextHintInPrompt()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Implement feature with cached context");
        var issueId = state.Issues[^1].Id;

        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        await executor.SpawnIssueAsync(issueId, "The orchestrator already confirmed this should follow decision #12 and reuse the release naming.", "fake", TimeSpan.FromSeconds(30), CancellationToken.None);

        var prompt = agent.LastPrompt ?? throw new InvalidOperationException("Expected spawned issue prompt to be captured.");
        Assert.That(prompt.Contains("Supplemental caller context:", StringComparison.Ordinal),
            $"Expected prompt to include supplemental caller context heading but got: {prompt}");
        Assert.That(prompt.Contains("follow decision #12", StringComparison.Ordinal),
            $"Expected prompt to include contextHint content but got: {prompt}");
        Assert.That(prompt.Contains("The issue record and linked decisions remain the source of truth.", StringComparison.Ordinal),
            $"Expected prompt to clarify contextHint precedence but got: {prompt}");
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
        Assert.That(runArtifact.Contains("tests/FeatureTests.cs", StringComparison.Ordinal),
            $"Expected run artifact to mention changed file, got: {runArtifact}");
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
        Assert.That(run.Status == AgentRunStatus.Blocked,
            $"Expected run status Blocked for graceful cancellation, but status was {run.Status}");
        Assert.That(run.Summary.Contains("Cancelled by user request.", StringComparison.OrdinalIgnoreCase),
            $"Expected persisted run summary to mention graceful cancellation but was: {run.Summary}");
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
        var expectedRoleKey = $"developer#{issueId}";
        Assert.That(reportedRoles.All(r => string.Equals(r, expectedRoleKey, StringComparison.OrdinalIgnoreCase)),
            $"Expected all tokens attributed to '{expectedRoleKey}' but got: {string.Join(", ", reportedRoles.Distinct())}");
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

    private static async Task DetailedVerbosity_PassesHooksToRequest()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Implement feature X");
        var issueId = state.Issues[^1].Id;
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issueId],
            Rationale = "test"
        };

        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Detailed
        };

        await executor.RunAsync(state, options);

        Assert.That(agent.Requests.Count >= 1, $"Expected at least 1 request but got {agent.Requests.Count}");
        var request = agent.Requests.Last();
        Assert.That(request.Hooks is not null, "Expected Hooks to be set on request when verbosity is Detailed");
        Assert.That(request.Hooks!.OnPreToolUse is not null, "Expected OnPreToolUse hook to be populated at Detailed verbosity");
        Assert.That(request.Hooks.OnPostToolUse is not null, "Expected OnPostToolUse hook to be populated at Detailed verbosity");
    }

    private static async Task NormalVerbosity_DoesNotPassHooksToRequest()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Implement feature Y");
        var issueId = state.Issues[^1].Id;
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issueId],
            Rationale = "test"
        };

        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Normal
        };

        await executor.RunAsync(state, options);

        var request = agent.Requests.Last();
        Assert.That(request.Hooks is null, "Expected Hooks to be null on request when verbosity is Normal");
    }

    private static async Task DetailedVerbosity_HooksLogPreAndPostToolUse()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Hooks log verification", "developer");
        var issueId = state.Issues[^1].Id;
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issueId],
            Rationale = "test"
        };

        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var logLines = new List<string>();
        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Detailed
        };

        await executor.RunAsync(state, options, log: logLines.Add);

        // Verify the hooks were built with the right tool logging format by invoking them directly
        var hooks = agent.Requests.Last().Hooks!;
        var toolArgsPayload = "{\"pattern\":\"foo\"}";
        var toolResultPayload = "3 matches";
        hooks.OnPreToolUse!("grep", toolArgsPayload);
        hooks.OnPostToolUse!("grep", "{}", toolResultPayload);

        Assert.That(logLines.Any(l => l.Contains("[tool↓]") && l.Contains("grep")),
            $"Expected pre-tool log line with '[tool↓]' and 'grep'. Log: {string.Join("|", logLines)}");
        Assert.That(logLines.Any(l => l.Contains("[tool↑]") && l.Contains("grep")),
            $"Expected post-tool log line with '[tool↑]' and 'grep'. Log: {string.Join("|", logLines)}");
        Assert.That(logLines.Any(l => string.Equals(l, $"    [developer#{issueId}][tool↓] grep", StringComparison.Ordinal)),
            $"Expected sanitized pre-tool log line with only tool name. Log: {string.Join("|", logLines)}");
        Assert.That(logLines.Any(l => string.Equals(l, $"    [developer#{issueId}][tool↑] grep", StringComparison.Ordinal)),
            $"Expected sanitized post-tool log line with only tool name. Log: {string.Join("|", logLines)}");
        Assert.That(logLines.All(l => !l.Contains(toolArgsPayload, StringComparison.Ordinal)),
            $"Detailed logging should not include raw tool args. Log: {string.Join("|", logLines)}");
        Assert.That(logLines.All(l => !l.Contains(toolResultPayload, StringComparison.Ordinal)),
            $"Detailed logging should not include raw tool results. Log: {string.Join("|", logLines)}");
    }

    private static async Task DetailedVerbosity_HooksAbortOnError()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Hooks error handling", "developer");
        var issueId = state.Issues[^1].Id;
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [issueId],
            Rationale = "test"
        };

        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Detailed
        };

        await executor.RunAsync(state, options, log: _ => { });

        var hooks = agent.Requests.Last().Hooks!;
        var decision = hooks.OnErrorOccurred!("tool", "fatal");
        Assert.That(decision == ErrorHandlingDecision.Abort,
            $"Expected detailed hook errors to abort, got: {decision}");
    }

    private static async Task NormalVerbosity_OrchestratorRequest_PassesVisibilityHooks()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Implement feature with orchestration", "developer");
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState();

        var orchestratorOutput = "OUTCOME: completed\nSUMMARY:\nOrchestrator done.";
        var workerOutput = "OUTCOME: completed\nSUMMARY:\nWorker done.";
        var agent = new RecordingAgentClient(orchestratorOutput, workerOutput);
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Normal
        };

        await executor.RunAsync(state, options, log: _ => { });

        Assert.That(agent.Requests.Count >= 1, $"Expected at least one request but got {agent.Requests.Count}");
        var orchestratorRequest = agent.Requests[0];
        Assert.That(orchestratorRequest.Hooks is not null,
            "Expected orchestrator request to carry visibility hooks at Normal verbosity.");
        Assert.That(orchestratorRequest.Hooks!.OnPreToolUse is not null,
            "Expected orchestrator visibility pre-tool hook to be present.");
        Assert.That(orchestratorRequest.Hooks.OnPostToolUse is not null,
            "Expected orchestrator visibility post-tool hook to be present.");
    }

    private static async Task OrchestratorVisibilityHooks_LogSpawnAndInlineEvents()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Orchestrator visibility", "developer");
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState();

        var orchestratorOutput = "OUTCOME: completed\nSUMMARY:\nOrchestrator done.";
        var workerOutput = "OUTCOME: completed\nSUMMARY:\nWorker done.";
        var agent = new RecordingAgentClient(orchestratorOutput, workerOutput);
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var logLines = new List<string>();
        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Normal
        };

        await executor.RunAsync(state, options, log: logLines.Add);

        var hooks = agent.Requests[0].Hooks!;
        hooks.OnPreToolUse!("spawn_agent", "{\"issueId\":42}");
        hooks.OnPostToolUse!("spawn_agent", "{\"issueId\":42}", "Issue #42 completed");
        hooks.OnPreToolUse!("task", "{\"prompt\":\"scan\"}");
        hooks.OnPostToolUse!("task", "{\"prompt\":\"scan\"}", "done");

        Assert.That(logLines.Any(l => l.Contains("[orchestrator][spawn] starting issue #42", StringComparison.Ordinal)),
            $"Expected spawn start visibility log, got: {string.Join("|", logLines)}");
        Assert.That(logLines.Any(l => l.Contains("[orchestrator][spawn] completed issue #42", StringComparison.Ordinal)),
            $"Expected spawn completion visibility log, got: {string.Join("|", logLines)}");
        Assert.That(logLines.Any(l => l.Contains("[orchestrator][inline] running inline subagent task", StringComparison.Ordinal)),
            $"Expected inline start visibility log, got: {string.Join("|", logLines)}");
        Assert.That(logLines.Any(l => l.Contains("[orchestrator][inline] inline subagent task finished", StringComparison.Ordinal)),
            $"Expected inline completion visibility log, got: {string.Join("|", logLines)}");
    }

    private static async Task OrchestratorVisibilityHooks_AbortOnError()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildStateWithIssue(store, "Orchestrator error handling", "developer");
        state.Phase = WorkflowPhase.Execution;
        state.ExecutionSelection = new ExecutionSelectionState();

        var orchestratorOutput = "OUTCOME: completed\nSUMMARY:\nOrchestrator done.";
        var workerOutput = "OUTCOME: completed\nSUMMARY:\nWorker done.";
        var agent = new RecordingAgentClient(orchestratorOutput, workerOutput);
        var factory = new FuncAgentClientFactory(_ => agent);
        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, fileSystem: fs);

        var options = new LoopExecutionOptions
        {
            MaxIterations = 1,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            Verbosity = LoopVerbosity.Normal
        };

        await executor.RunAsync(state, options, log: _ => { });

        var hooks = agent.Requests[0].Hooks!;
        var decision = hooks.OnErrorOccurred!("tool", "fatal");
        Assert.That(decision == ErrorHandlingDecision.Abort,
            $"Expected orchestrator visibility hook errors to abort, got: {decision}");
    }
}
