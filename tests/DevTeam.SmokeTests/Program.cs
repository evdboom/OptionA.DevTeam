using DevTeam.Cli;
using DevTeam.Core;

var tests = new List<(string Name, Action Run)>
{
    ("RunOnce bootstraps and queues planner work", TestRunOnceBootstrapsAndQueues),
    ("Completed run unlocks dependent architect work", TestCompleteRunUnlocksDependency),
    ("Planning phase waits for plan approval", TestPlanningPhaseRequiresApproval),
    ("Blocking questions only halt when no ready work exists", TestBlockingQuestionWaitState),
    ("Premium cap forces fallback model", TestPremiumCapFallback),
    ("Role suggested model overrides default policy", TestRoleSuggestedModelOverridesDefaultPolicy),
    ("CLI agent client builds a copilot invocation", TestCliAgentClientInvocationShape),
    ("SDK agent client is the default integration backend", TestSdkAgentClientFactory),
    ("Tool update check detects newer stable versions", TestToolUpdateCheckDetectsNewerStableVersions),
    ("Tool update command targets the global package", TestToolUpdateCommandTargetsGlobalPackage),
    ("SDK session config wires workspace MCP server", TestSdkSessionConfigWiresWorkspaceMcp),
    ("SDK session config wires external MCP servers", TestSdkSessionConfigWiresExternalMcpServers),
    ("Workspace loads MCP server definitions", TestWorkspaceLoadsMcpServers),
    ("Planning session is reused across planning retries", TestPlanningSessionIsReusedAcrossPlanningRetries),
    ("Issue retries reuse the same session", TestIssueRetriesReuseTheSameSession),
    ("Parallel pipelines keep isolated sessions", TestParallelPipelinesKeepIsolatedSessions),
    ("Plan workflow generates plan when missing", TestPlanWorkflowGeneratesPlanWhenMissing),
    ("Plan workflow blocks run before planning", TestPlanWorkflowBlocksRunBeforePlanning),
    ("Workspace loads default modes", TestWorkspaceLoadsModes),
    ("Set mode updates active mode and pipeline defaults", TestSetModeUpdatesRuntimeConfiguration),
    ("Set keep-awake updates runtime configuration", TestSetKeepAwakeUpdatesRuntimeConfiguration),
    ("Goal input resolver loads markdown from file", TestGoalInputResolverLoadsMarkdownFromFile),
    ("DevTeam assets load from .devteam-source first", TestPromptAssetsPreferDevTeamSource),
    ("Run-loop executes queued work with normal verbosity", TestRunLoopExecutesWork),
    ("Execution loop uses orchestrator-selected batch", TestExecutionLoopUsesOrchestratorSelectedBatch),
    ("Run-loop resumes previously queued runs", TestRunLoopResumesQueuedRuns),
    ("Run-loop persists agent questions for user input", TestRunLoopPersistsQuestions),
    ("Planning run writes plan artifact for approval", TestPlanningRunWritesPlanArtifact),
    ("Init clears stale legacy workspace artifacts", TestInitClearsLegacyArtifacts),
    ("Planning feedback reopens the planning issue", TestPlanningFeedbackReopensPlanning),
    ("Agent-generated issues keep the loop moving", TestAgentGeneratedIssues),
    ("Issue board markdown mirrors workspace state", TestIssueBoardMirror),
    ("Generated issue roles are normalized", TestGeneratedIssueRoleNormalization),
    ("Role aliases are exposed for validation feedback", TestRoleAliasesExposed),
    ("Parallel loop executes independent areas concurrently", TestParallelLoopExecutesIndependentAreas),
    ("Conflict prevention avoids same-area parallel runs", TestConflictPreventionAvoidsSameAreaRuns),
    ("Architect pipeline completion creates developer follow-up", TestArchitectPipelineCompletionCreatesDeveloperFollowUp),
    ("Developer pipeline completion creates tester follow-up", TestDeveloperPipelineCompletionCreatesTesterFollowUp),
    ("Architect work gates non-architect execution", TestArchitectWorkGatesNonArchitectExecution),
    ("Priority gap can reduce pipeline concurrency", TestPriorityGapReducesPipelineConcurrency),
    ("Mode guardrails appear in agent prompt", TestModeGuardrailsAppearInPrompt),
    ("Pipeline handoff appears in agent prompt", TestPipelineHandoffAppearsInPrompt),
    ("Collapsed response headers still parse cleanly", TestCollapsedResponseHeadersParseCleanly),
    ("Run artifacts capture superpowers and tools used", TestRunArtifactsCaptureUsageMetadata),
    ("Legacy workspaces hydrate missing roles and superpowers", TestLegacyWorkspaceHydratesMetadata),
    ("Workspace loads legacy execution selection timestamps", TestWorkspaceLoadsLegacyExecutionSelectionTimestamp),
    ("Friendly role names resolve to canonical roles", TestFriendlyRoleNamesResolve),
    ("External repos fall back to packaged prompt assets", TestExternalReposFallBackToPackagedAssets),
    ("Git helper initializes repository when missing", TestGitHelperInitializesRepository),
    ("Git helper stages only iteration changes", TestGitHelperStagesOnlyIterationChanges),
    ("Loop stages changed files after iteration", TestLoopStagesChangedFilesAfterIteration),
    ("Workspace manifest shards large collections", TestWorkspaceManifestShardsCollections),
    ("Concurrent workspace saves remain parseable", TestConcurrentWorkspaceSavesRemainParseable),
    ("Workspace save tolerates replace-blocking readers", TestWorkspaceSaveToleratesReplaceBlockingReaders),
    ("Prompt asset bodies are not persisted in state files", TestPromptAssetsAreNotPersisted),
    ("Execution orchestrator emits heartbeat while selecting batch", TestExecutionOrchestratorEmitsHeartbeat),
    ("Existing pipeline follow-ups are normalized", TestExistingPipelineFollowUpsAreNormalized),
    ("Plan approval transitions to architect planning", TestPlanApprovalTransitionsToArchitectPlanning),
    ("Architect approval transitions to execution", TestArchitectApprovalTransitionsToExecution),
    ("Auto-approve skips both approval gates", TestAutoApproveSkipsBothGates),
    ("Autopilot mode enables auto-approve", TestAutopilotModeEnablesAutoApprove),
    ("Design-only roles receive file boundary enforcement", TestDesignOnlyRolesReceiveFileBoundary),
    ("Planner cannot create duplicate architect issues", TestPlannerCannotCreateDuplicateArchitectIssues)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL: {name}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke tests failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"  - {failure}");
    }
    return 1;
}

Console.WriteLine($"All {tests.Count} smoke tests passed.");
return 0;

static void TestRunOnceBootstrapsAndQueues()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");

    var result = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual("queued", result.State, "Loop state");
    AssertEqual(1, result.QueuedRuns.Count, "Queued run count");
    AssertEqual("planner", result.QueuedRuns[0].RoleSlug, "Queued role");
    AssertEqual(1, harness.State.Roadmap.Count, "Roadmap count");
    AssertEqual(2, harness.State.Issues.Count, "Issue count");
}

static void TestCompleteRunUnlocksDependency()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");

    var first = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, first.QueuedRuns[0].RunId, "completed", "Planning finished.");
    harness.Runtime.ApprovePlan(harness.State, "Approved the initial plan.");
    var second = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual("architect", second.QueuedRuns[0].RoleSlug, "Dependent role");
}

static void TestPlanningPhaseRequiresApproval()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");

    var first = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, first.QueuedRuns[0].RunId, "completed", "Planning finished.");
    var blocked = harness.Runtime.RunOnce(harness.State, 1);

    AssertEqual("awaiting-plan-approval", blocked.State, "Loop state");
}

static void TestBlockingQuestionWaitState()
{
    using var harness = new TestHarness();
    harness.Runtime.AddQuestion(harness.State, "Need production API key?", true);

    var result = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual("waiting-for-user", result.State, "Loop state");
}

static void TestPremiumCapFallback()
{
    using var harness = new TestHarness();
    harness.State.Budget.PremiumCreditCap = 0;
    harness.Runtime.ApprovePlan(harness.State, "Use execution mode for reviewer work.");
    harness.Runtime.AddIssue(harness.State, "Review architecture", "", "reviewer", 100, null, []);

    var result = harness.Runtime.RunOnce(harness.State, 1);

    AssertEqual("gpt-5.4", result.QueuedRuns[0].ModelName, "Fallback model");
}

static void TestRoleSuggestedModelOverridesDefaultPolicy()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Use execution mode for frontend work.");
    harness.Runtime.AddIssue(harness.State, "Implement HUD", "", "frontend-developer", 100, null, []);

    var result = harness.Runtime.RunOnce(harness.State, 1);

    AssertEqual("claude-sonnet-4.6", result.QueuedRuns[0].ModelName, "Role suggested model should take precedence.");
}

static void TestCliAgentClientInvocationShape()
{
    using var harness = new TestHarness();
    var fakeRunner = new FakeCommandRunner();
    var client = new CopilotCliAgentClient(fakeRunner);
    var result = client.InvokeAsync(new AgentInvocationRequest
    {
        Prompt = "Summarize this repository",
        Model = "gpt-5.4",
        WorkingDirectory = harness.RepoRoot,
        Timeout = TimeSpan.FromSeconds(15),
        ExtraArguments = ["--allow-all"]
    }).GetAwaiter().GetResult();

    AssertEqual("copilot-cli", result.BackendName, "Backend name");
    AssertEqual("copilot", fakeRunner.LastSpec?.FileName, "Process file name");
    AssertTrue(fakeRunner.LastSpec?.Arguments.SequenceEqual(
        ["--allow-all", "--model", "gpt-5.4", "--no-ask-user", "-p", "Summarize this repository"]) == true,
        "Argument list should match the CLI invocation.");
}

static void TestSdkAgentClientFactory()
{
    var client = AgentClientFactory.Create("sdk");
    AssertEqual("copilot-sdk", client.Name, "SDK backend name");
}

static void TestToolUpdateCheckDetectsNewerStableVersions()
{
    var status = ToolUpdateService.EvaluateVersions("0.1.18+abc123", ["0.1.17", "0.1.18", "0.1.19", "0.2.0-preview.1"]);

    AssertTrue(status.IsUpdateAvailable, "A newer stable package version should be detected.");
    AssertEqual("0.1.18", status.CurrentVersion, "Build metadata should be ignored in the installed version.");
    AssertEqual("0.1.19", status.LatestVersion, "Prerelease versions should not win over the newest stable release.");
}

static void TestToolUpdateCommandTargetsGlobalPackage()
{
    var arguments = ToolUpdateService.BuildGlobalUpdateArguments("0.1.19");

    AssertTrue(arguments.SequenceEqual(["tool", "update", "--global", "OptionA.DevTeam", "--version", "0.1.19"]),
        "The update command should target the global OptionA.DevTeam tool package.");
}

static void TestSdkSessionConfigWiresWorkspaceMcp()
{
    using var harness = new TestHarness();
    var request = new AgentInvocationRequest
    {
        Prompt = "Summarize the work.",
        Model = "gpt-5.4",
        SessionId = "architect-run-001",
        WorkingDirectory = harness.RepoRoot,
        WorkspacePath = harness.Store.WorkspacePath,
        EnableWorkspaceMcp = true,
        WorkspaceMcpServerName = "devteam-workspace",
        ToolHostPath = @"C:\tools\DevTeam.Cli.dll"
    };

    var sessionConfig = WorkspaceMcpSessionConfigFactory.BuildSessionConfig(request);
    var mcpServers = sessionConfig.McpServers ?? throw new InvalidOperationException("Session config should include MCP servers.");
    AssertTrue(mcpServers.ContainsKey("devteam-workspace"), "Workspace MCP server should be registered.");
    var server = mcpServers["devteam-workspace"] as GitHub.Copilot.SDK.McpLocalServerConfig;
    AssertTrue(server is not null, "Workspace MCP server config should be local.");
    AssertEqual("dotnet", server!.Command, "DLL-hosted MCP server should launch with dotnet.");
    AssertTrue(server.Args.Contains("workspace-mcp"), "MCP command should invoke the workspace host.");
    AssertTrue(server.Args.Contains(harness.Store.WorkspacePath), "MCP command should target the workspace path.");
}

static void TestSdkSessionConfigWiresExternalMcpServers()
{
    using var harness = new TestHarness();
    var request = new AgentInvocationRequest
    {
        Prompt = "Look up library docs.",
        Model = "gpt-5.4",
        SessionId = "dev-run-001",
        WorkingDirectory = harness.RepoRoot,
        EnableWorkspaceMcp = false,
        ExternalMcpServers =
        [
            new McpServerDefinition
            {
                Name = "context7",
                Command = "npx",
                Args = ["-y", "@upstash/context7-mcp@latest"],
                Enabled = true
            },
            new McpServerDefinition
            {
                Name = "disabled-server",
                Command = "npx",
                Args = ["-y", "some-disabled-mcp"],
                Enabled = false
            }
        ]
    };

    var sessionConfig = WorkspaceMcpSessionConfigFactory.BuildSessionConfig(request);
    var mcpServers = sessionConfig.McpServers ?? throw new InvalidOperationException("Session config should include external MCP servers.");
    AssertTrue(mcpServers.ContainsKey("context7"), "Context7 MCP server should be registered.");
    AssertTrue(!mcpServers.ContainsKey("disabled-server"), "Disabled MCP server should not be registered.");
    var context7 = mcpServers["context7"] as GitHub.Copilot.SDK.McpLocalServerConfig;
    AssertTrue(context7 is not null, "Context7 config should be a local MCP server.");
    AssertEqual("npx", context7!.Command, "Context7 should launch via npx.");
    AssertTrue(context7.Args.Contains("@upstash/context7-mcp@latest"), "Context7 should reference the correct package.");
}

static void TestWorkspaceLoadsMcpServers()
{
    using var harness = new TestHarness();

    AssertTrue(harness.State.McpServers.Count >= 1, "Workspace should load MCP server definitions from MCP_SERVERS.json.");
    AssertTrue(harness.State.McpServers.Any(s => s.Name == "context7"), "Context7 MCP server should be loaded.");
    var context7 = harness.State.McpServers.First(s => s.Name == "context7");
    AssertEqual("npx", context7.Command, "Context7 command should be npx.");
    AssertTrue(context7.Enabled, "Context7 should be enabled by default.");
}

static void TestWorkspaceLoadsModes()
{
    using var harness = new TestHarness();

    AssertTrue(harness.State.Modes.Count >= 2, "Workspace should load at least the default modes.");
    AssertTrue(harness.State.Modes.Any(mode => mode.Slug == "develop"), "Develop mode should be available.");
    AssertTrue(harness.State.Modes.Any(mode => mode.Slug == "creative-writing"), "Creative writing mode should be available.");
    AssertEqual("develop", harness.State.Runtime.ActiveModeSlug, "Develop should be the default active mode.");
}

static void TestPlanningSessionIsReusedAcrossPlanningRetries()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a reusable planning workflow.");
    harness.Store.Save(harness.State);
    var agent = new RecordingAgentClient(
        "OUTCOME: completed\nSUMMARY:\nInitial planning pass complete.",
        "OUTCOME: completed\nSUMMARY:\nPlanning updated after feedback.");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => agent);

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    harness.Runtime.RecordPlanningFeedback(harness.State, "Narrow the first milestone.");
    harness.Store.Save(harness.State);

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual(2, agent.Requests.Count, "Expected two planner invocations.");
    AssertEqual(agent.Requests[0].SessionId, agent.Requests[1].SessionId, "Planning retries should reuse the planning session.");
}

static void TestIssueRetriesReuseTheSameSession()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Implement core loop", "Build the first pass.", "developer", 100, null, []);
    harness.Store.Save(harness.State);
    var agent = new RecordingAgentClient(
        "OUTCOME: failed\nSUMMARY:\nNeed another pass.",
        "OUTCOME: completed\nSUMMARY:\nCompleted on retry.");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => agent);

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    var workerRequests = agent.Requests
        .Where(item => item.Prompt.Contains("Current issue:", StringComparison.Ordinal))
        .ToList();
    AssertEqual(2, workerRequests.Count, "Expected two worker issue invocations.");
    AssertEqual(workerRequests[0].SessionId, workerRequests[1].SessionId, "Issue retries should stay on the same session.");
}

static void TestParallelPipelinesKeepIsolatedSessions()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Plan gameplay", "", "architect", 100, null, [], "gameplay");
    harness.Runtime.AddIssue(harness.State, "Plan menus", "", "architect", 95, null, [], "menus");
    harness.Store.Save(harness.State);
    var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nPlanned.");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => agent);

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 2,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    var workerRequests = agent.Requests
        .Where(item => item.Prompt.Contains("Current issue:", StringComparison.Ordinal))
        .ToList();
    AssertEqual(2, workerRequests.Count, "Expected two worker invocations after orchestration.");
    AssertTrue(workerRequests.Select(item => item.SessionId).Distinct(StringComparer.Ordinal).Count() == 2,
        "Separate pipelines should not share a session.");
}

static void TestPlanWorkflowGeneratesPlanWhenMissing()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a plan-first shell.");
    harness.Store.Save(harness.State);

    var result = PlanWorkflow.EnsurePlanAsync(
        harness.Store,
        harness.State,
        _ =>
        {
            File.WriteAllText(PlanWorkflow.GetPlanPath(harness.Store), "# Plan\n\nGenerated.");
            return Task.FromResult(new LoopExecutionReport { IterationsExecuted = 1, FinalState = "awaiting-plan-approval" });
        }).GetAwaiter().GetResult();

    AssertEqual(PlanPreparationStatus.Generated, result.Status, "Missing plan should be generated.");
    AssertTrue(PlanWorkflow.HasPlan(harness.Store), "Generated plan should be written to disk.");
}

static void TestPlanWorkflowBlocksRunBeforePlanning()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a plan-first shell.");
    harness.Store.Save(harness.State);

    AssertTrue(PlanWorkflow.RequiresPlanningBeforeRun(harness.State, harness.Store), "Run commands should be blocked until a plan exists.");

    File.WriteAllText(PlanWorkflow.GetPlanPath(harness.Store), "# Plan\n\nReady for review.");

    AssertTrue(!PlanWorkflow.RequiresPlanningBeforeRun(harness.State, harness.Store), "Run commands should stop blocking once a plan exists.");
}

static void TestSetModeUpdatesRuntimeConfiguration()
{
    using var harness = new TestHarness();

    harness.Runtime.SetMode(harness.State, "creative-writing");

    AssertEqual("creative-writing", harness.State.Runtime.ActiveModeSlug, "Mode should update.");
    AssertTrue(harness.State.Runtime.DefaultPipelineRoles.SequenceEqual(["architect", "developer", "reviewer"]),
        "Creative writing mode should swap the default pipeline roles.");
}

static void TestSetKeepAwakeUpdatesRuntimeConfiguration()
{
    using var harness = new TestHarness();

    AssertTrue(!harness.State.Runtime.KeepAwakeEnabled, "Keep-awake should be disabled by default.");
    harness.Runtime.SetKeepAwake(harness.State, true);

    AssertTrue(harness.State.Runtime.KeepAwakeEnabled, "Keep-awake should be enabled after the runtime update.");
}

static void TestGoalInputResolverLoadsMarkdownFromFile()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var goalPath = Path.Combine(tempRoot, "goal.md");
        File.WriteAllText(goalPath, """
# Vision
Build an agent-native programming language.

# Context
- Must be understandable by coding agents
- Must run broadly
""");

        var goal = GoalInputResolver.Resolve(null, "goal.md", tempRoot);

        AssertTrue(goal is not null, "Goal file should resolve to goal text.");
        var resolvedGoal = goal ?? throw new InvalidOperationException("Goal file should resolve to goal text.");
        AssertTrue(resolvedGoal.Contains("# Vision", StringComparison.Ordinal), "Goal file markdown should be preserved.");
        AssertTrue(resolvedGoal.Contains("understandable by coding agents", StringComparison.Ordinal), "Goal file body should be loaded.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestPromptAssetsPreferDevTeamSource()
{
    using var harness = new TestHarness();
    var role = harness.State.Roles.FirstOrDefault(item => item.Slug == "developer")
        ?? throw new InvalidOperationException("Developer role should be present.");
    var superpower = harness.State.Superpowers.FirstOrDefault(item => item.Slug == "verify")
        ?? throw new InvalidOperationException("Verify superpower should be present.");

    AssertTrue(role.SourcePath.StartsWith(".devteam-source", StringComparison.OrdinalIgnoreCase),
        "Roles should load from .devteam-source when present.");
    AssertTrue(superpower.SourcePath.StartsWith(".devteam-source", StringComparison.OrdinalIgnoreCase),
        "Superpowers should load from .devteam-source when present.");
}

static void TestRunLoopExecutesWork()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
    var planning = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Planning finished.");
    harness.Runtime.ApprovePlan(harness.State, "Approved the initial plan.");

    // Complete the architect planning phase
    var architectRun = harness.Runtime.RunOnce(harness.State, 1);
    AssertTrue(architectRun.QueuedRuns.Count > 0, "Architect planning should queue architect work.");
    AssertEqual("architect", architectRun.QueuedRuns[0].RoleSlug, "Architect planning phase should run architect issues.");
    harness.Runtime.CompleteRun(harness.State, architectRun.QueuedRuns[0].RunId, "completed", "Architecture decided.");
    harness.Runtime.ApproveArchitectPlan(harness.State, "Approve the detailed plan.");

    harness.Store.Save(harness.State);
    var messages = new List<string>();
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nCompleted the assigned task."));

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 4,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        },
        messages.Add).GetAwaiter().GetResult();

    AssertEqual("idle", report.FinalState, "Final loop state");
    AssertTrue(messages.Any(message => message.Contains("Iteration 1", StringComparison.Ordinal)),
        "Normal verbosity should emit iteration logs.");
    AssertTrue(messages.Any(message => message.Contains("Execution", StringComparison.Ordinal)),
        "Normal verbosity should include the current phase.");
    AssertTrue(harness.State.Decisions.Count >= 3, "Loop should persist decisions.");
    AssertTrue(harness.State.AgentRuns.Any(run => !string.IsNullOrWhiteSpace(run.SessionId)),
        "Runs should record session ids.");
    AssertTrue(Directory.GetFiles(Path.Combine(harness.Store.WorkspacePath, "decisions"), "decision-*.md").Length > 0,
        "Decision artifacts should be written to the workspace.");
}

static void TestExecutionLoopUsesOrchestratorSelectedBatch()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    var selectedIssue = harness.Runtime.AddIssue(harness.State, "Gameplay UI", "", "frontend-developer", 90, null, [], "ui");
    var skippedIssue = harness.Runtime.AddIssue(harness.State, "Gameplay audio", "", "frontend-developer", 100, null, [], "audio");
    harness.Store.Save(harness.State);
    var client = new RecordingAgentClient(
        $"""
OUTCOME: completed
SUMMARY:
Run the UI first.
SELECTED_ISSUES:
- {selectedIssue.Id}
ISSUES:
(none)
SUPERPOWERS_USED:
- plan
TOOLS_USED:
- list_ready_issues
QUESTIONS:
(none)
""",
        """
OUTCOME: completed
SUMMARY:
Done.
ISSUES:
(none)
SUPERPOWERS_USED:
(none)
TOOLS_USED:
(none)
QUESTIONS:
(none)
""");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => client);

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual("queued", report.FinalState, "Loop should queue and run the orchestrator-selected batch.");
    AssertTrue(harness.State.AgentRuns.Any(run => run.IssueId == selectedIssue.Id), "Selected issue should be queued.");
    AssertTrue(harness.State.AgentRuns.All(run => run.IssueId != skippedIssue.Id), "Non-selected issue should remain unqueued.");
}

static void TestRunLoopResumesQueuedRuns()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
    var queued = harness.Runtime.RunOnce(harness.State, 1);
    AssertEqual("queued", queued.State, "Initial queue state");
    harness.Store.Save(harness.State);
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nPlanning finished."));

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual("queued", report.FinalState, "Loop should execute already queued work");
    AssertEqual(AgentRunStatus.Completed, harness.State.AgentRuns.Single().Status, "Queued run should complete");
    AssertEqual(ItemStatus.Done, harness.State.Issues.Single(issue => issue.IsPlanningIssue).Status, "Planning issue should complete");
}

static void TestRunLoopPersistsQuestions()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
    var queued = harness.Runtime.RunOnce(harness.State, 1);
    AssertEqual("queued", queued.State, "Initial queue state");
    harness.Store.Save(harness.State);
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("""
OUTCOME: blocked
SUMMARY:
Need a decision before continuing.
QUESTIONS:
- [blocking] Should the game use pixel art or vector art?
"""));

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 2,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual("waiting-for-user", report.FinalState, "Loop should wait for user after a blocking question");
    AssertEqual(1, harness.State.Questions.Count(item => item.Status == QuestionStatus.Open), "Open question count");
    AssertEqual(ItemStatus.Blocked, harness.State.Issues.Single(issue => issue.IsPlanningIssue).Status, "Planning issue should be blocked");
    AssertTrue(File.ReadAllText(Path.Combine(harness.Store.WorkspacePath, "questions.md")).Contains("pixel art or vector art", StringComparison.Ordinal),
        "Questions file should contain the generated question.");

    harness.Runtime.AnswerQuestion(harness.State, harness.State.Questions.Single().Id, "Use pixel art.");
    AssertEqual(ItemStatus.Open, harness.State.Issues.Single(issue => issue.IsPlanningIssue).Status, "Answering the question should reopen blocked work");
}

static void TestPlanningRunWritesPlanArtifact()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
    var queued = harness.Runtime.RunOnce(harness.State, 1);
    AssertEqual("queued", queued.State, "Initial queue state");
    harness.Store.Save(harness.State);
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("""
OUTCOME: completed
SUMMARY:
Build a small HTML5 Canvas game first, then add physics, obstacles, collision handling, score tracking, and playtesting.
QUESTIONS:
(none)
"""));

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 2,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual("awaiting-plan-approval", report.FinalState, "Loop should stop for plan approval");
    var planPath = Path.Combine(harness.Store.WorkspacePath, "plan.md");
    AssertTrue(File.Exists(planPath), "Planning should write a plan artifact");
    AssertTrue(File.ReadAllText(planPath).Contains("Build a small HTML5 Canvas game first", StringComparison.Ordinal),
        "Plan artifact should include the planning summary.");
}

static void TestInitClearsLegacyArtifacts()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(tempRoot, ".devteam", "issues"));
    File.WriteAllText(Path.Combine(tempRoot, ".devteam", "issues", "_index.md"), "# stale");
    File.WriteAllText(Path.Combine(tempRoot, ".devteam", "plan.md"), "# stale plan");
    try
    {
        var repoRoot = TestHarness.FindRepoRootForTests();
        var store = new WorkspaceStore(Path.Combine(tempRoot, ".devteam"));
        store.Initialize(repoRoot, 25, 6);
        var indexPath = Path.Combine(tempRoot, ".devteam", "issues", "_index.md");
        AssertTrue(File.Exists(indexPath), "Init should recreate the generated issue index.");
        AssertTrue(!File.ReadAllText(indexPath).Contains("# stale", StringComparison.Ordinal),
            "Init should replace stale legacy issue artifacts with the generated board.");
        AssertTrue(File.Exists(Path.Combine(tempRoot, ".devteam", "questions.md")),
            "Init should recreate current workspace artifacts.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestPlanningFeedbackReopensPlanning()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
    var queued = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, queued.QueuedRuns[0].RunId, "completed", "Initial plan ready.");

    harness.Runtime.RecordPlanningFeedback(harness.State, "Make the first milestone just the game scaffold.");

    AssertEqual(ItemStatus.Open, harness.State.Issues.Single(issue => issue.IsPlanningIssue).Status, "Planning feedback should reopen the planning issue");
    AssertTrue(harness.State.Decisions.Any(item => item.Source == "plan-feedback"),
        "Planning feedback should be persisted as a decision.");
}

static void TestAgentGeneratedIssues()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
    var planning = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Initial plan ready.");
    harness.Runtime.ApprovePlan(harness.State, "Approved.");
    harness.Store.Save(harness.State);
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("""
OUTCOME: completed
SUMMARY:
Architecture is defined. Implementation can proceed in small steps.
ISSUES:
- role=frontend-developer; priority=95; depends=none; title=Create HTML5 Canvas game scaffold; detail=Create the scaffold and render loop.
QUESTIONS:
(none)
"""));

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual("queued", report.FinalState, "Architect work should have executed");
    AssertTrue(harness.State.Issues.Any(issue => issue.Title == "Create HTML5 Canvas game scaffold"),
        "Generated issues should be added to the workspace state.");
}

static void TestIssueBoardMirror()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
    var issue = harness.Runtime.AddIssue(
        harness.State,
        "Create HTML5 Canvas game scaffold",
        "Create the scaffold and render loop.",
        "frontend-developer",
        95,
        null,
        []);
    harness.Runtime.RunOnce(harness.State, 1);
    harness.Store.Save(harness.State);

    var indexPath = Path.Combine(harness.Store.WorkspacePath, "issues", "_index.md");
    var issuePath = Path.Combine(harness.Store.WorkspacePath, "issues", "0001-create-html5-canvas-game-scaffold.md");
    var index = File.ReadAllText(indexPath);
    var issueText = File.ReadAllText(issuePath);

    AssertTrue(index.Contains("Create HTML5 Canvas game scaffold", StringComparison.Ordinal),
        "Issue index should list the issue.");
    AssertTrue(issueText.Contains("## Latest Run", StringComparison.Ordinal),
        "Issue file should include latest run details.");
    AssertTrue(issueText.Contains("frontend-developer", StringComparison.Ordinal),
        "Issue file should include the role.");
}

static void TestGeneratedIssueRoleNormalization()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");

    var created = harness.Runtime.AddGeneratedIssues(
        harness.State,
        harness.Runtime.RunOnce(harness.State, 1).QueuedRuns[0].IssueId,
        [
            new GeneratedIssueProposal
            {
                Title = "Build game loop",
                Detail = "Implement the frame loop.",
                RoleSlug = "engineer",
                Priority = 80
            }
        ]);

    AssertEqual("developer", created.Single().RoleSlug, "Unknown alias role should normalize");
}

static void TestRoleAliasesExposed()
{
    using var harness = new TestHarness();

    var aliases = harness.Runtime.GetKnownRoleAliases(harness.State);
    var knownRoles = harness.Runtime.GetKnownRoleSlugs(harness.State);
    var exactResolved = harness.Runtime.TryResolveRoleSlug(harness.State, "developer", out var exactRole);
    var aliasResolved = harness.Runtime.TryResolveRoleSlug(harness.State, "engineer", out var aliasRole);

    AssertTrue(knownRoles.Contains("developer"), "Known roles should include developer.");
    AssertEqual("developer", aliases["engineer"], "Engineer alias should map to developer.");
    AssertTrue(exactResolved, "Exact roles should validate successfully.");
    AssertEqual("developer", exactRole, "Exact role should stay canonical.");
    AssertTrue(!aliasResolved, "Alias should not count as canonical validation success.");
    AssertEqual("developer", aliasRole, "Alias should resolve to canonical role.");
}

static void TestParallelLoopExecutesIndependentAreas()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Build render loop", "Implement the frame loop.", "developer", 90, null, [], "rendering");
    harness.Runtime.AddIssue(harness.State, "Add score tests", "Test score transitions.", "tester", 85, null, [], "testing");

    var agent = new FakeConcurrentAgentClient("OUTCOME: completed\nSUMMARY:\nCompleted the assigned task.");
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => agent);

    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 2,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    AssertEqual("queued", report.FinalState, "Parallel iteration should execute queued work.");
    AssertTrue(agent.MaxConcurrentInvocations >= 2, "Independent areas should run concurrently.");
    AssertEqual(2, harness.State.Issues.Count(item => item.Status == ItemStatus.Done), "Both independent issues should complete.");
}

static void TestConflictPreventionAvoidsSameAreaRuns()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Build bird entity", "Implement bird state.", "developer", 90, null, [], "gameplay");
    harness.Runtime.AddIssue(harness.State, "Tune flap physics", "Adjust gravity and flap impulse.", "developer", 85, null, [], "gameplay");
    harness.Runtime.AddIssue(harness.State, "Add score tests", "Test scoring.", "tester", 80, null, [], "testing");

    var result = harness.Runtime.RunOnce(harness.State, 3);

    AssertEqual("queued", result.State, "Ready work should be queued.");
    AssertEqual(2, result.QueuedRuns.Count, "Only non-conflicting work should be queued together.");
    AssertEqual(1, result.QueuedRuns.Count(run => run.Area == "gameplay"), "Only one gameplay issue should queue in parallel.");
    AssertEqual(1, result.QueuedRuns.Count(run => run.Area == "testing"), "Independent area should still queue.");
}

static void TestArchitectPipelineCompletionCreatesDeveloperFollowUp()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    var architectIssue = harness.Runtime.AddIssue(harness.State, "Design gameplay slice", "Outline the gameplay slice.", "architect", 100, null, [], "gameplay");

    var queued = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, queued.QueuedRuns.Single().RunId, "completed", "Architecture complete.");

    var developerIssue = harness.State.Issues.SingleOrDefault(issue => issue.ParentIssueId == architectIssue.Id && issue.RoleSlug == "developer");
    AssertTrue(developerIssue is not null, "Architect completion should create a developer follow-up.");
    AssertTrue(developerIssue!.DependsOnIssueIds.Contains(architectIssue.Id), "Developer follow-up should depend on the architect issue.");
    AssertTrue(developerIssue.PipelineId == architectIssue.PipelineId, "Follow-up should stay in the same pipeline.");
    AssertEqual("Implement gameplay slice", developerIssue.Title, "Developer follow-up title should reflect the implementation stage.");
    AssertTrue(developerIssue.Detail.Contains("Implement gameplay slice", StringComparison.Ordinal), "Developer follow-up detail should reflect the implementation stage.");
}

static void TestDeveloperPipelineCompletionCreatesTesterFollowUp()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    var developerIssue = harness.Runtime.AddIssue(harness.State, "Create HTML5 Canvas game scaffold with bird physics", "Create the scaffold and render loop.", "frontend-developer", 100, null, [], "game-core");

    var queued = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, queued.QueuedRuns.Single().RunId, "completed", "Implementation complete.");

    var testerIssue = harness.State.Issues.SingleOrDefault(issue => issue.ParentIssueId == developerIssue.Id && issue.RoleSlug == "tester");
    AssertTrue(testerIssue is not null, "Developer completion should create a tester follow-up.");
    AssertTrue(testerIssue!.DependsOnIssueIds.Contains(developerIssue.Id), "Tester follow-up should depend on the developer issue.");
    AssertTrue(testerIssue.PipelineId == developerIssue.PipelineId, "Follow-up should stay in the same pipeline.");
    AssertEqual("Test HTML5 Canvas game scaffold with bird physics", testerIssue.Title, "Tester follow-up title should reflect the validation stage.");
    AssertTrue(testerIssue.Detail.Contains("Test HTML5 Canvas game scaffold with bird physics", StringComparison.Ordinal), "Tester follow-up detail should reflect the validation stage.");
}

static void TestArchitectWorkGatesNonArchitectExecution()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Architecture pass", "", "architect", 80, null, [], "shared");
    harness.Runtime.AddIssue(harness.State, "Frontend implementation", "", "frontend-developer", 100, null, [], "ui");

    var result = harness.Runtime.RunOnce(harness.State, 2);

    AssertEqual(1, result.QueuedRuns.Count, "Architect gating should reduce the batch to architect work.");
    AssertEqual("architect", result.QueuedRuns.Single().RoleSlug, "Architect work should run before non-architect execution.");
}

static void TestPriorityGapReducesPipelineConcurrency()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Critical architecture", "", "architect", 100, null, [], "alpha");
    harness.Runtime.AddIssue(harness.State, "Lower priority implementation", "", "developer", 70, null, [], "beta");
    harness.Runtime.AddIssue(harness.State, "Lower priority tests", "", "tester", 68, null, [], "gamma");

    var result = harness.Runtime.RunOnce(harness.State, 3);

    AssertEqual(1, result.QueuedRuns.Count, "Large priority gaps should allow the orchestrator to run a single pipeline.");
    AssertEqual("Critical architecture", result.QueuedRuns.Single().Title, "The highest priority pipeline should win.");
}

static void TestModeGuardrailsAppearInPrompt()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Build gameplay loop", "Create a playable gameplay loop.", "developer", 100, null, []);
    harness.Store.Save(harness.State);
    var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => agent);

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    var prompt = agent.LastPrompt ?? throw new InvalidOperationException("Expected a prompt to be captured.");
    AssertTrue(prompt.Contains("Active mode:", StringComparison.Ordinal), "Prompt should include the active mode.");
    AssertTrue(prompt.Contains("Always build the changed project or solution", StringComparison.Ordinal), "Develop guardrails should be included in the prompt.");
    AssertTrue(prompt.Contains("unit tests, integration tests", StringComparison.Ordinal), "Testing guardrails should be visible to the agent.");
    AssertTrue(prompt.Contains(".gitignore", StringComparison.Ordinal), "Develop guardrails should require repo hygiene.");
    AssertTrue(prompt.Contains("README.md", StringComparison.Ordinal), "Develop guardrails should require runnable documentation.");
}

static void TestPipelineHandoffAppearsInPrompt()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    var architectIssue = harness.Runtime.AddIssue(harness.State, "Plan gameplay loop", "Define the gameplay architecture.", "architect", 100, null, [], "gameplay");
    var queued = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, queued.QueuedRuns.Single().RunId, "completed", "Use a simple game loop, obstacle spawner, and a shared collision model.");
    harness.Store.Save(harness.State);
    var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nImplemented the gameplay loop.");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => agent);

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    var prompt = agent.LastPrompt ?? throw new InvalidOperationException("Expected a prompt to be captured.");
    AssertTrue(prompt.Contains("Pipeline handoff context:", StringComparison.Ordinal), "Prompt should include the pipeline handoff section.");
    AssertTrue(prompt.Contains($"issue #{architectIssue.Id} [architect] Plan gameplay loop", StringComparison.Ordinal), "Prompt should identify the prior architect stage.");
    AssertTrue(prompt.Contains("Use a simple game loop, obstacle spawner, and a shared collision model.", StringComparison.Ordinal), "Prompt should surface the prior stage summary.");
}

static void TestCollapsedResponseHeadersParseCleanly()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Scaffold app", "Create the project scaffold.", "developer", 90, null, []);
    harness.Store.Save(harness.State);
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("OUTCOME: completedSUMMARY:\nScaffolded the app.\nISSUES:\n(none)\nQUESTIONS:\n(none)"));

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    var run = harness.State.AgentRuns.Single();
    var artifact = File.ReadAllText(Path.Combine(harness.Store.WorkspacePath, "runs", "run-001.md"));

    AssertEqual("Scaffolded the app.", run.Summary, "Summary should be parsed cleanly.");
    AssertTrue(!artifact.Contains("## Summary\n\nOUTCOME:", StringComparison.Ordinal), "Run artifact summary should not duplicate raw structured output.");
    AssertTrue(artifact.Contains("## Output", StringComparison.Ordinal), "Run artifact should still preserve raw output.");
}

static void TestRunArtifactsCaptureUsageMetadata()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    harness.Runtime.AddIssue(harness.State, "Implement game loop", "Build the loop.", "developer", 90, null, []);
    harness.Store.Save(harness.State);
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("""
OUTCOME: completed
SUMMARY:
Implemented the game loop.
ISSUES:
(none)
SUPERPOWERS_USED:
- plan
- verify
TOOLS_USED:
- dotnet
- node
QUESTIONS:
(none)
"""));

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        }).GetAwaiter().GetResult();

    var run = harness.State.AgentRuns.Single();
    var runArtifact = File.ReadAllText(Path.Combine(harness.Store.WorkspacePath, "runs", "run-001.md"));
    var issueArtifact = File.ReadAllText(Path.Combine(harness.Store.WorkspacePath, "issues", "0001-implement-game-loop.md"));

    AssertTrue(run.SuperpowersUsed.SequenceEqual(["plan", "verify"]), "Run should capture used superpowers.");
    AssertTrue(run.ToolsUsed.SequenceEqual(["dotnet", "node"]), "Run should capture used tools.");
    AssertTrue(runArtifact.Contains("## Superpowers Used", StringComparison.Ordinal), "Run artifact should include superpowers.");
    AssertTrue(runArtifact.Contains("- plan", StringComparison.Ordinal), "Run artifact should list used superpowers.");
    AssertTrue(runArtifact.Contains("## Tools Used", StringComparison.Ordinal), "Run artifact should include tools.");
    AssertTrue(issueArtifact.Contains("Superpowers Used: plan, verify", StringComparison.Ordinal), "Issue mirror should include superpower usage.");
    AssertTrue(issueArtifact.Contains("Tools Used: dotnet, node", StringComparison.Ordinal), "Issue mirror should include tool usage.");
}

static void TestLegacyWorkspaceHydratesMetadata()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var repoRoot = TestHarness.FindRepoRootForTests();
        var store = new WorkspaceStore(Path.Combine(tempRoot, ".devteam"));
        var state = store.Initialize(repoRoot, 25, 6);
        state.Roles = [];
        state.Superpowers = [];
        File.WriteAllText(
            store.StatePath,
            System.Text.Json.JsonSerializer.Serialize(
                state,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var loaded = store.Load();
        var persistedJson = File.ReadAllText(store.StatePath);

        AssertTrue(loaded.Roles.Count > 0, "Legacy workspace should rehydrate roles on load.");
        AssertTrue(loaded.Superpowers.Count > 0, "Legacy workspace should rehydrate superpowers on load.");
        AssertTrue(persistedJson.Contains("\"FormatVersion\": 4", StringComparison.Ordinal), "Hydrated workspace should be migrated to the current manifest format.");
        AssertTrue(!File.Exists(Path.Combine(store.StateDirectoryPath, "roles.json")), "Derived role assets should not be persisted into state files.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestWorkspaceLoadsLegacyExecutionSelectionTimestamp()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var repoRoot = TestHarness.FindRepoRootForTests();
        var store = new WorkspaceStore(Path.Combine(tempRoot, ".devteam"));
        var state = store.Initialize(repoRoot, 25, 6);
        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = [3, 8],
            Rationale = "Legacy orchestrator selection.",
            SessionId = "legacy-session",
            UpdatedAtUtc = new DateTimeOffset(2026, 03, 20, 11, 34, 50, TimeSpan.Zero)
        };
        store.Save(state);

        var manifest = File.ReadAllText(store.StatePath)
            .Replace("\"UpdatedAtUtc\": \"2026-03-20T11:34:50.0000000+00:00\"", "\"UpdatedAtUtc\": \"20/03/2026 11:34:50\"", StringComparison.Ordinal);
        File.WriteAllText(store.StatePath, manifest);

        var loaded = store.Load();
        store.Save(loaded);
        var persistedJson = File.ReadAllText(store.StatePath);

        AssertTrue(loaded.ExecutionSelection.SelectedIssueIds.SequenceEqual([3, 8]), "Legacy execution selection should load.");
        AssertEqual("legacy-session", loaded.ExecutionSelection.SessionId, "Legacy execution selection session should load.");
        AssertTrue(persistedJson.Contains("\"UpdatedAtUtc\": \"2026-03-20T11:34:50.0000000", StringComparison.Ordinal), "Saving after load should rewrite the timestamp in round-trip format.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestFriendlyRoleNamesResolve()
{
    using var harness = new TestHarness();

    var resolved = harness.Runtime.TryResolveRoleSlug(harness.State, "Front-end developer", out var role);

    AssertTrue(resolved, "Friendly role name should resolve successfully.");
    AssertEqual("frontend-developer", role, "Friendly role name should map to canonical slug.");
}

static void TestExternalReposFallBackToPackagedAssets()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    var repoRoot = Path.Combine(tempRoot, "target-repo");
    Directory.CreateDirectory(repoRoot);
    try
    {
        var store = new WorkspaceStore(Path.Combine(repoRoot, ".devteam"));
        var state = store.Initialize(repoRoot, 25, 6);

        AssertTrue(state.Roles.Count > 0, "External repos without local .devteam-source should still load roles.");
        AssertTrue(state.Superpowers.Count > 0, "External repos without local .devteam-source should still load superpowers.");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
        {
            TestFileSystem.DeleteDirectoryWithRetries(tempRoot);
        }
    }
}

static void TestGitHelperInitializesRepository()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        AssertTrue(!GitWorkspace.IsGitRepository(tempRoot), "Temp folder should not start as a git repository.");

        var initialized = GitWorkspace.EnsureRepository(tempRoot);

        AssertTrue(initialized, "Git helper should initialize a repository when one is missing.");
        AssertTrue(Directory.Exists(Path.Combine(tempRoot, ".git")), "Git init should create the .git directory.");
        AssertTrue(GitWorkspace.IsGitRepository(tempRoot), "Folder should be recognized as a git repository after init.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestGitHelperStagesOnlyIterationChanges()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        GitWorkspace.EnsureRepository(tempRoot);
        File.WriteAllText(Path.Combine(tempRoot, "preexisting.txt"), "before");
        var before = GitWorkspace.TryCaptureStatus(tempRoot);
        File.WriteAllText(Path.Combine(tempRoot, "iteration.txt"), "after");

        var staged = GitWorkspace.StagePathsChangedSince(tempRoot, before);
        var status = RunGit(tempRoot, "status", "--porcelain=v1");

        AssertTrue(staged.SequenceEqual(["iteration.txt"]), "Only the new iteration path should be staged.");
        AssertTrue(status.Contains("A  iteration.txt", StringComparison.Ordinal), "Iteration file should be staged.");
        AssertTrue(status.Contains("?? preexisting.txt", StringComparison.Ordinal), "Preexisting untracked file should remain unstaged.");
    }
    finally
    {
        // Leave the temp repo behind to avoid flaky Windows file locking on .git objects during teardown.
    }
}

static void TestLoopStagesChangedFilesAfterIteration()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var repoRoot = Path.Combine(tempRoot, "repo");
        Directory.CreateDirectory(repoRoot);
        GitWorkspace.EnsureRepository(repoRoot);

        var localStore = new WorkspaceStore(Path.Combine(tempRoot, ".loop-workspace"));
        var localState = localStore.Initialize(repoRoot, 25, 6);
        var localRuntime = new DevTeamRuntime();
        localRuntime.ApprovePlan(localState, "Run in execution mode.");
        localRuntime.AddIssue(localState, "Write output file", "", "developer", 100, null, []);
        localStore.Save(localState);

        var executor = new LoopExecutor(
            localRuntime,
            localStore,
            _ => new FileWritingAgentClient("generated.txt", "OUTCOME: completed\nSUMMARY:\nDone."));

        executor.RunAsync(
            localState,
            new LoopExecutionOptions
            {
                Backend = "sdk",
                MaxIterations = 1,
                MaxSubagents = 1,
                Verbosity = LoopVerbosity.Normal
            }).GetAwaiter().GetResult();

        var status = RunGit(repoRoot, "status", "--porcelain=v1");
        AssertTrue(status.Contains("A  generated.txt", StringComparison.Ordinal), "Loop should stage files changed during the iteration.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestConcurrentWorkspaceSavesRemainParseable()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var repoRoot = TestHarness.FindRepoRootForTests();
        var store = new WorkspaceStore(Path.Combine(tempRoot, ".devteam"));
        var state = store.Initialize(repoRoot, 25, 6);
        for (var index = 0; index < 40; index++)
        {
            state.Issues.Add(new IssueItem
            {
                Id = state.NextIssueId++,
                Title = $"Issue {index}",
                Detail = new string('x', 200),
                RoleSlug = "developer",
                Priority = 50
            });
        }
        store.Save(state);

        var storeA = new WorkspaceStore(store.WorkspacePath);
        var storeB = new WorkspaceStore(store.WorkspacePath);
        var stateA = storeA.Load();
        var stateB = storeB.Load();
        var gate = new ManualResetEventSlim(false);

        var taskA = Task.Run(() =>
        {
            gate.Wait();
            for (var index = 0; index < 10; index++)
            {
                stateA.Budget.CreditsCommitted = index;
                storeA.Save(stateA);
            }
        });

        var taskB = Task.Run(() =>
        {
            gate.Wait();
            for (var index = 0; index < 10; index++)
            {
                stateB.Budget.PremiumCreditsCommitted = index;
                storeB.Save(stateB);
            }
        });

        gate.Set();
        Task.WaitAll(taskA, taskB);

        var loaded = store.Load();
        using var manifestJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.StatePath));
        using var issuesJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(store.StateDirectoryPath, "issues.json")));

        AssertTrue(loaded.Issues.Count >= 40, "Concurrent saves should leave the workspace parseable.");
        AssertTrue(manifestJson.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object, "Manifest should remain valid JSON.");
        AssertTrue(issuesJson.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array, "Issues collection should remain valid JSON.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestWorkspaceManifestShardsCollections()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a large autonomous system.");
    harness.Runtime.AddIssue(harness.State, "Design runtime", "Create the runtime plan.", "architect", 90, null, []);
    harness.Runtime.AddQuestion(harness.State, "Should the project support plugins?", false);
    harness.Store.Save(harness.State);

    var manifestJson = File.ReadAllText(harness.Store.StatePath);
    var issuesJson = File.ReadAllText(Path.Combine(harness.Store.StateDirectoryPath, "issues.json"));
    var questionsJson = File.ReadAllText(Path.Combine(harness.Store.StateDirectoryPath, "questions.json"));

    AssertTrue(manifestJson.Contains("\"FormatVersion\": 4", StringComparison.Ordinal), "Workspace should persist a manifest format version.");
    AssertTrue(manifestJson.Contains("\"IssuesFile\": \"issues.json\"", StringComparison.Ordinal), "Manifest should point at the sharded issues file.");
    AssertTrue(!manifestJson.Contains("\"Issues\":", StringComparison.Ordinal), "Manifest should not inline issue collections.");
    AssertTrue(issuesJson.Contains("Design runtime", StringComparison.Ordinal), "Issues should be stored in the sharded issues file.");
    AssertTrue(questionsJson.Contains("support plugins", StringComparison.Ordinal), "Questions should be stored in the sharded questions file.");
}

static void TestWorkspaceSaveToleratesReplaceBlockingReaders()
{
    var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        var repoRoot = TestHarness.FindRepoRootForTests();
        var store = new WorkspaceStore(Path.Combine(tempRoot, ".devteam"));
        var state = store.Initialize(repoRoot, 25, 6);
        store.Save(state);

        var questionsPath = Path.Combine(store.WorkspacePath, "questions.md");
        using var reader = new FileStream(questionsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        state.Questions.Add(new QuestionItem
        {
            Id = state.NextQuestionId++,
            Text = "How should the player start the game?",
            Status = QuestionStatus.Open,
            IsBlocking = false
        });

        store.Save(state);

        var questionsText = File.ReadAllText(questionsPath);
        AssertTrue(questionsText.Contains("How should the player start the game?", StringComparison.Ordinal),
            "Workspace save should succeed even when replace-style swaps are blocked by an open reader.");
    }
    finally
    {
        TryCleanupTempRepo(tempRoot);
    }
}

static void TestPromptAssetsAreNotPersisted()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Keep persisted context small.");
    harness.Store.Save(harness.State);

    var manifestJson = File.ReadAllText(harness.Store.StatePath);
    var rolesPath = Path.Combine(harness.Store.StateDirectoryPath, "roles.json");
    var superpowersPath = Path.Combine(harness.Store.StateDirectoryPath, "superpowers.json");

    AssertTrue(harness.State.Roles.Count > 0, "Roles should still be available in memory for prompt building.");
    AssertTrue(harness.State.Superpowers.Count > 0, "Superpowers should still be available in memory for prompt building.");
    AssertTrue(!manifestJson.Contains("## Suggested Model", StringComparison.Ordinal), "Manifest should not inline prompt markdown bodies.");
    AssertTrue(!File.Exists(rolesPath), "Roles should not be persisted into the state directory.");
    AssertTrue(!File.Exists(superpowersPath), "Superpowers should not be persisted into the state directory.");
}

static void TestExecutionOrchestratorEmitsHeartbeat()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    var issue = harness.Runtime.AddIssue(harness.State, "Plan gameplay architecture", "", "architect", 100, null, [], "gameplay");
    harness.Store.Save(harness.State);
    var messages = new List<string>();
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeStaggeredAgentClient("""
OUTCOME: completed
SUMMARY:
Run the architect batch first.
SELECTED_ISSUES:
- ISSUE_ID
ISSUES:
(none)
SUPERPOWERS_USED:
(none)
TOOLS_USED:
(none)
QUESTIONS:
(none)
""".Replace("ISSUE_ID", issue.Id.ToString(), StringComparison.Ordinal), TimeSpan.FromMilliseconds(150), TimeSpan.Zero));

    executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 1,
            MaxSubagents = 1,
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            Verbosity = LoopVerbosity.Normal
        },
        messages.Add).GetAwaiter().GetResult();

    var heartbeatIndex = messages.FindIndex(message => message.Contains("Still running execution orchestrator", StringComparison.Ordinal));
    var outcomeIndex = messages.FindIndex(message => message.Contains("Execution orchestrator outcome:", StringComparison.Ordinal));

    AssertTrue(heartbeatIndex >= 0, "Execution orchestrator should emit a heartbeat while selecting a batch.");
    AssertTrue(outcomeIndex > heartbeatIndex, "Execution orchestrator outcome should be logged after the heartbeat.");
}

static void TestExistingPipelineFollowUpsAreNormalized()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
    var architectIssue = harness.Runtime.AddIssue(harness.State, "Design gameplay slice", "Outline the gameplay slice.", "architect", 100, null, [], "gameplay");

    var queued = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, queued.QueuedRuns.Single().RunId, "completed", "Architecture complete.");

    var developerIssue = harness.State.Issues.Single(issue => issue.ParentIssueId == architectIssue.Id && issue.RoleSlug == "developer");
    developerIssue.Title = architectIssue.Title;
    developerIssue.Detail = architectIssue.Detail;

    _ = harness.Runtime.BuildStatusReport(harness.State);

    AssertEqual("Implement gameplay slice", developerIssue.Title, "Existing inherited follow-up titles should be normalized.");
    AssertTrue(developerIssue.Detail.Contains("Implement gameplay slice", StringComparison.Ordinal), "Existing inherited follow-up details should be normalized.");
}

static void TestPlanApprovalTransitionsToArchitectPlanning()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a thing.");
    var planning = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Plan done.");

    // Before approval, we're in Planning
    AssertEqual(WorkflowPhase.Planning, harness.State.Phase, "Should be in Planning before approval");

    harness.Runtime.ApprovePlan(harness.State, "Looks good.");

    // After approval, should transition to ArchitectPlanning because there's an open architect issue
    AssertEqual(WorkflowPhase.ArchitectPlanning, harness.State.Phase, "Should transition to ArchitectPlanning after plan approval");

    // Architect issues should be eligible in this phase
    var architectRun = harness.Runtime.RunOnce(harness.State, 1);
    AssertTrue(architectRun.QueuedRuns.Count > 0, "Architect planning should queue architect work.");
    AssertEqual("architect", architectRun.QueuedRuns[0].RoleSlug, "Only architect issues should run in ArchitectPlanning phase.");
}

static void TestArchitectApprovalTransitionsToExecution()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a thing.");
    var planning = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Plan done.");
    harness.Runtime.ApprovePlan(harness.State, "Looks good.");

    // Complete architect work
    var architectRun = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, architectRun.QueuedRuns[0].RunId, "completed", "Architecture decided.");

    AssertEqual(WorkflowPhase.ArchitectPlanning, harness.State.Phase, "Should still be in ArchitectPlanning before second approval");

    harness.Runtime.ApproveArchitectPlan(harness.State, "Start building.");
    AssertEqual(WorkflowPhase.Execution, harness.State.Phase, "Should transition to Execution after architect approval");
}

static void TestAutoApproveSkipsBothGates()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a thing.");
    harness.Runtime.SetAutoApprove(harness.State, true);
    AssertTrue(harness.State.Runtime.AutoApproveEnabled, "Auto-approve should be enabled.");

    // Complete the planning issue
    var planning = harness.Runtime.RunOnce(harness.State, 1);
    harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Plan done.");
    harness.Store.Save(harness.State);

    // Run the loop — it should auto-approve both the plan and architect approvals
    var executor = new LoopExecutor(
        harness.Runtime,
        harness.Store,
        _ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nDone."));

    var messages = new List<string>();
    var report = executor.RunAsync(
        harness.State,
        new LoopExecutionOptions
        {
            Backend = "sdk",
            MaxIterations = 6,
            MaxSubagents = 1,
            Verbosity = LoopVerbosity.Normal
        },
        messages.Add).GetAwaiter().GetResult();

    AssertTrue(messages.Any(message => message.Contains("Auto-approving plan", StringComparison.Ordinal)),
        "Loop should auto-approve the high-level plan.");
    AssertTrue(messages.Any(message => message.Contains("Auto-approving architect plan", StringComparison.Ordinal)),
        "Loop should auto-approve the architect plan.");
    AssertEqual(WorkflowPhase.Execution, harness.State.Phase, "Should reach Execution phase via auto-approve.");
}

static void TestAutopilotModeEnablesAutoApprove()
{
    using var harness = new TestHarness();
    AssertTrue(!harness.State.Runtime.AutoApproveEnabled, "Auto-approve should be off by default.");

    harness.Runtime.SetMode(harness.State, "autopilot");
    AssertEqual("autopilot", harness.State.Runtime.ActiveModeSlug, "Active mode should be autopilot.");
    AssertTrue(harness.State.Runtime.AutoApproveEnabled, "Autopilot mode should enable auto-approve.");

    harness.Runtime.SetMode(harness.State, "develop");
    AssertTrue(!harness.State.Runtime.AutoApproveEnabled, "Switching away from autopilot should disable auto-approve.");
}

static void TestDesignOnlyRolesReceiveFileBoundary()
{
    using var harness = new TestHarness();
    harness.Runtime.ApprovePlan(harness.State, "Approve.");

    // Architect should get file boundary enforcement
    var architectIssue = harness.Runtime.AddIssue(harness.State, "Design the architecture", "Choose patterns.", "architect", 100, null, []);
    harness.Store.Save(harness.State);
    var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDesigned.");
    var executor = new LoopExecutor(harness.Runtime, harness.Store, _ => agent);
    executor.RunAsync(harness.State, new LoopExecutionOptions { Backend = "sdk", MaxIterations = 1, MaxSubagents = 1, Verbosity = LoopVerbosity.Normal }).GetAwaiter().GetResult();
    var architectPrompt = agent.LastPrompt ?? throw new InvalidOperationException("Expected architect prompt.");
    AssertTrue(architectPrompt.Contains("FILE BOUNDARY", StringComparison.Ordinal), "Architect prompt should include FILE BOUNDARY enforcement.");
    AssertTrue(architectPrompt.Contains("design-only role", StringComparison.Ordinal), "Architect prompt should state it is a design-only role.");

    // Developer should NOT get file boundary enforcement
    var devIssue = harness.Runtime.AddIssue(harness.State, "Build the game loop", "Implement it.", "developer", 80, null, []);
    harness.Store.Save(harness.State);
    var devAgent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nBuilt.");
    var devExecutor = new LoopExecutor(harness.Runtime, harness.Store, _ => devAgent);
    devExecutor.RunAsync(harness.State, new LoopExecutionOptions { Backend = "sdk", MaxIterations = 1, MaxSubagents = 1, Verbosity = LoopVerbosity.Normal }).GetAwaiter().GetResult();
    var devPrompt = devAgent.LastPrompt ?? throw new InvalidOperationException("Expected developer prompt.");
    AssertTrue(!devPrompt.Contains("FILE BOUNDARY", StringComparison.Ordinal), "Developer prompt should NOT include FILE BOUNDARY enforcement.");
}

static void TestPlannerCannotCreateDuplicateArchitectIssues()
{
    using var harness = new TestHarness();
    harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
    var planning = harness.Runtime.RunOnce(harness.State, 1);

    // State is Planning, bootstrap already created one architect issue.
    var architectCountBefore = harness.State.Issues.Count(i =>
        string.Equals(i.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
    AssertEqual(1, architectCountBefore, "Bootstrap should seed exactly one architect issue");

    // Simulate the planner proposing an architect issue via AddGeneratedIssues.
    var planningIssueId = planning.QueuedRuns[0].IssueId;
    var added = harness.Runtime.AddGeneratedIssues(harness.State, planningIssueId, [
        new GeneratedIssueProposal
        {
            Title = "Choose technology stack and design architecture",
            Detail = "Evaluate options and choose the best approach.",
            RoleSlug = "architect",
            Area = "architecture",
            Priority = 90
        }
    ]);

    AssertEqual(0, added.Count, "Planner should not be able to add architect issues during Planning");
    var architectCountAfter = harness.State.Issues.Count(i =>
        string.Equals(i.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
    AssertEqual(1, architectCountAfter, "Architect issue count should remain 1");
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}' but got '{actual}'.");
    }
}

static void AssertTrue(bool value, string message)
{
    if (!value)
    {
        throw new InvalidOperationException(message);
    }
}

static string RunGit(string workingDirectory, params string[] arguments)
{
    using var process = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }
    };

    foreach (var argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    if (!process.Start())
    {
        throw new InvalidOperationException("Failed to start git in tests.");
    }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Git command failed in tests." : stderr.Trim());
    }

    return stdout;
}

/// <summary>
/// Best-effort cleanup for temp git repos. On Windows, .git object locking can cause
/// flaky failures, so we retry but never fail the test. On Linux/CI, cleanup should
/// succeed reliably and avoid accumulating temp dirs.
/// </summary>
static void TryCleanupTempRepo(string path)
{
    if (!Directory.Exists(path))
    {
        return;
    }

    try
    {
        TestFileSystem.DeleteDirectoryWithRetries(path);
    }
    catch
    {
        // Best-effort: on Windows, .git object files can remain locked.
    }
}

file sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
        RepoRoot = FindRepoRootForTests();
        Store = new WorkspaceStore(Path.Combine(TempRoot, ".devteam"));
        State = Store.Initialize(RepoRoot, 25, 6);
        Runtime = new DevTeamRuntime();
    }

    public string TempRoot { get; }
    public string RepoRoot { get; }
    public WorkspaceStore Store { get; }
    public WorkspaceState State { get; }
    public DevTeamRuntime Runtime { get; }

    public void Dispose()
    {
        if (Directory.Exists(TempRoot))
        {
            TestFileSystem.DeleteDirectoryWithRetries(TempRoot);
        }
    }

    internal static string FindRepoRootForTests()
    {
        var directory = AppContext.BaseDirectory;
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".devteam-source")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}

file sealed class FakeCommandRunner : ICommandRunner
{
    public CommandExecutionSpec? LastSpec { get; private set; }

    public Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default)
    {
        LastSpec = spec;
        return Task.FromResult(new CommandExecutionResult
        {
            ExitCode = 0,
            StdOut = "ok"
        });
    }
}

file sealed class FakeAgentClient(string output) : IAgentClient
{
    public string Name => "fake-agent";

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            StdOut = output
        });
    }
}

file sealed class SwitchingAgentClient(Func<AgentInvocationRequest, string> selector) : IAgentClient
{
    public string Name => "switching-agent";

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            SessionId = request.SessionId ?? "",
            ExitCode = 0,
            StdOut = selector(request)
        });
    }
}

file sealed class RecordingAgentClient : IAgentClient
{
    private readonly IReadOnlyList<string> _outputs;
    private readonly object _gate = new();
    private int _invocationCount;

    public RecordingAgentClient(params string[] outputs)
    {
        _outputs = outputs.Length == 0
            ? ["OUTCOME: completed\nSUMMARY:\nDone."]
            : outputs;
    }

    public string Name => "recording-agent";
    public string? LastPrompt { get; private set; }
    public List<RecordedAgentRequest> Requests { get; } = [];

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        string output;
        lock (_gate)
        {
            LastPrompt = request.Prompt;
            Requests.Add(new RecordedAgentRequest
            {
                Prompt = request.Prompt,
                SessionId = request.SessionId ?? "",
                Model = request.Model ?? ""
            });
            output = _outputs[Math.Min(_invocationCount, _outputs.Count - 1)];
            _invocationCount++;
        }

        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            SessionId = request.SessionId ?? "",
            StdOut = output
        });
    }
}

file sealed class RecordedAgentRequest
{
    public string Prompt { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string Model { get; init; } = "";
}

file sealed class FileWritingAgentClient(string fileName, string output) : IAgentClient
{
    public string Name => "file-writing-agent";

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(Path.Combine(request.WorkingDirectory, fileName), "generated");
        return Task.FromResult(new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            StdOut = output
        });
    }
}

file sealed class FakeConcurrentAgentClient(string output) : IAgentClient
{
    private int _currentInvocations;

    public string Name => "fake-concurrent-agent";
    public int MaxConcurrentInvocations { get; private set; }

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        var current = Interlocked.Increment(ref _currentInvocations);
        MaxConcurrentInvocations = Math.Max(MaxConcurrentInvocations, current);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            return new AgentInvocationResult
            {
                BackendName = Name,
                ExitCode = 0,
                StdOut = output
            };
        }
        finally
        {
            Interlocked.Decrement(ref _currentInvocations);
        }
    }
}

file sealed class FakeStaggeredAgentClient(string output, params TimeSpan[] delays) : IAgentClient
{
    private int _invocationCount;

    public string Name => "fake-staggered-agent";

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default)
    {
        var invocation = Interlocked.Increment(ref _invocationCount) - 1;
        var delay = invocation < delays.Length ? delays[invocation] : delays.LastOrDefault();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        return new AgentInvocationResult
        {
            BackendName = Name,
            ExitCode = 0,
            StdOut = output
        };
    }
}

file static class TestFileSystem
{
    public static void DeleteDirectoryWithRetries(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }

        try
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                    }
                }

                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
