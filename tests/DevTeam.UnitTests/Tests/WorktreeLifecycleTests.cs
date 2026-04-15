namespace DevTeam.UnitTests.Tests;

internal static class WorktreeLifecycleTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("WorktreeMode_Off_NoWorktreeCreated", WorktreeMode_Off_NoWorktreeCreated),
        new("WorktreeMode_On_CreatesWorktreePerRun", WorktreeMode_On_CreatesWorktreePerRun),
        new("WorktreeMode_On_MergesWorktreeAfterSuccess", WorktreeMode_On_MergesWorktreeAfterSuccess),
        new("WorktreeMode_On_MergeConflict_CreatesConflictIssue", WorktreeMode_On_MergeConflict_CreatesConflictIssue),
        new("WorktreeMode_On_SuccessfulMerge_RemovesWorktreeFromState", WorktreeMode_On_SuccessfulMerge_RemovesWorktreeFromState),
        new("WorktreeMode_On_UsesWorktreePath_AsWorkingDirectory", WorktreeMode_On_UsesWorktreePath_AsWorkingDirectory),
    ];

    private static WorkspaceState BuildReadyState(WorkspaceStore store, bool worktreeMode = false)
    {
        var state = store.Initialize("C:\\test-repo", 200, 50);
        state.Phase = WorkflowPhase.Execution;
        state.Runtime.WorktreeMode = worktreeMode;
        state.Runtime.WorkspaceMcpEnabled = false;

        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = "Implement feature",
            RoleSlug = "developer",
            Status = ItemStatus.Open,
            Priority = 50
        };
        state.Issues.Add(issue);
        store.Save(state);
        return state;
    }

    private static async Task WorktreeMode_Off_NoWorktreeCreated()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildReadyState(store, worktreeMode: false);

        var git = new FakeGitRepository();
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(
            "OUTCOME: completed\nSUMMARY:\nDone."));

        var options = new LoopExecutionOptions
        {
            Backend = "fake",
            MaxIterations = 1,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, git, fileSystem: fs);

        // Queue a run first
        var result = new DevTeamRuntime().QueueExecutionSelection(state);
        store.Save(state);

        await executor.RunAsync(state, options);

        Assert.That(git.CreatedWorktreePaths.Count == 0,
            $"Expected 0 worktrees created (mode off) but got {git.CreatedWorktreePaths.Count}");
    }

    private static async Task WorktreeMode_On_CreatesWorktreePerRun()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildReadyState(store, worktreeMode: true);

        var git = new FakeGitRepository();
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(
            "OUTCOME: completed\nSUMMARY:\nDone."));

        var options = new LoopExecutionOptions
        {
            Backend = "fake",
            MaxIterations = 2,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        // Queue the run
        new DevTeamRuntime().QueueExecutionSelection(state);
        store.Save(state);

        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, git, fileSystem: fs);
        await executor.RunAsync(state, options);

        Assert.That(git.CreatedWorktreePaths.Count >= 1,
            $"Expected at least 1 worktree created (mode on) but got {git.CreatedWorktreePaths.Count}");
    }

    private static async Task WorktreeMode_On_MergesWorktreeAfterSuccess()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildReadyState(store, worktreeMode: true);

        var git = new FakeGitRepository { MergeResult = new WorktreeMergeResult(false) };
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(
            "OUTCOME: completed\nSUMMARY:\nDone."));

        var options = new LoopExecutionOptions
        {
            Backend = "fake",
            MaxIterations = 2,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        new DevTeamRuntime().QueueExecutionSelection(state);
        store.Save(state);

        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, git, fileSystem: fs);
        await executor.RunAsync(state, options);

        Assert.That(git.MergedBranches.Count >= 1,
            $"Expected at least 1 branch merged but got {git.MergedBranches.Count}");
        Assert.That(git.RemovedWorktreePaths.Count >= 1,
            $"Expected at least 1 worktree removed after successful merge but got {git.RemovedWorktreePaths.Count}");
    }

    private static async Task WorktreeMode_On_MergeConflict_CreatesConflictIssue()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildReadyState(store, worktreeMode: true);

        var git = new FakeGitRepository { MergeResult = new WorktreeMergeResult(true, "src/Program.cs") };
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(
            "OUTCOME: completed\nSUMMARY:\nDone."));

        var options = new LoopExecutionOptions
        {
            Backend = "fake",
            MaxIterations = 2,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        new DevTeamRuntime().QueueExecutionSelection(state);
        store.Save(state);

        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, git, fileSystem: fs);
        await executor.RunAsync(state, options);

        // Load fresh state to check generated issues
        var fresh = store.Load();
        var conflictIssue = fresh.Issues.FirstOrDefault(i =>
            i.Title.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
            i.Title.Contains("Resolve", StringComparison.OrdinalIgnoreCase));
        Assert.That(conflictIssue is not null,
            "Expected a conflict-resolution issue to be created after a merge conflict");
    }

    private static async Task WorktreeMode_On_SuccessfulMerge_RemovesWorktreeFromState()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildReadyState(store, worktreeMode: true);

        var git = new FakeGitRepository { MergeResult = new WorktreeMergeResult(false) };
        var factory = new FuncAgentClientFactory(_ => new FakeAgentClient(
            "OUTCOME: completed\nSUMMARY:\nDone."));

        var options = new LoopExecutionOptions
        {
            Backend = "fake",
            MaxIterations = 2,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        new DevTeamRuntime().QueueExecutionSelection(state);
        store.Save(state);

        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, git, fileSystem: fs);
        await executor.RunAsync(state, options);

        var fresh = store.Load();
        Assert.That(fresh.Worktrees.Count == 0,
            $"Expected 0 remaining worktrees after successful merge but got {fresh.Worktrees.Count}");
    }

    private static async Task WorktreeMode_On_UsesWorktreePath_AsWorkingDirectory()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        var state = BuildReadyState(store, worktreeMode: true);

        var git = new FakeGitRepository();
        var recordingClient = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var factory = new FuncAgentClientFactory(_ => recordingClient);

        var options = new LoopExecutionOptions
        {
            Backend = "fake",
            MaxIterations = 2,
            MaxSubagents = 1,
            AgentTimeout = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(5)
        };

        new DevTeamRuntime().QueueExecutionSelection(state);
        store.Save(state);

        var executor = new LoopExecutor(new DevTeamRuntime(), store, factory, git, fileSystem: fs);
        await executor.RunAsync(state, options);

        // The RecordingAgentClient records the WorkingDirectory via the request
        Assert.That(recordingClient.Requests.Count >= 1,
            "Expected at least 1 agent invocation");
        var worktreePath = git.CreatedWorktreePaths.FirstOrDefault();
        if (worktreePath is not null && recordingClient.Requests.Count >= 1)
        {
            // When a worktree was created, the agent request's WorkingDirectory should use it
            Assert.That(
                recordingClient.Requests.Any(r => r.WorkingDirectory == worktreePath),
                $"Expected agent to run in worktree path '{worktreePath}' but requests used: {string.Join(", ", recordingClient.Requests.Select(r => r.WorkingDirectory))}");
        }
    }
}
