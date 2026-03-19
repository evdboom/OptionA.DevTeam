using DevTeam.Core;

var tests = new List<(string Name, Action Run)>
{
    ("RunOnce bootstraps and queues orchestrator work", TestRunOnceBootstrapsAndQueues),
    ("Completed run unlocks dependent architect work", TestCompleteRunUnlocksDependency),
    ("Planning phase waits for plan approval", TestPlanningPhaseRequiresApproval),
    ("Blocking questions only halt when no ready work exists", TestBlockingQuestionWaitState),
    ("Premium cap forces fallback model", TestPremiumCapFallback),
    ("CLI agent client builds a copilot invocation", TestCliAgentClientInvocationShape),
    ("SDK agent client is the default integration backend", TestSdkAgentClientFactory),
    ("DevTeam assets load from .devteam-source first", TestPromptAssetsPreferDevTeamSource),
    ("Run-loop executes queued work with normal verbosity", TestRunLoopExecutesWork),
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
    ("Collapsed response headers still parse cleanly", TestCollapsedResponseHeadersParseCleanly),
    ("Run artifacts capture superpowers and tools used", TestRunArtifactsCaptureUsageMetadata),
    ("Legacy workspaces hydrate missing roles and superpowers", TestLegacyWorkspaceHydratesMetadata),
    ("Friendly role names resolve to canonical roles", TestFriendlyRoleNamesResolve),
    ("External repos fall back to packaged prompt assets", TestExternalReposFallBackToPackagedAssets)
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
    AssertEqual("orchestrator", result.QueuedRuns[0].RoleSlug, "Queued role");
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
    AssertTrue(messages.Any(message => message.Contains("Phase: Execution", StringComparison.Ordinal)),
        "Normal verbosity should include the current phase.");
    AssertEqual(2, harness.State.Issues.Count(item => item.Status == ItemStatus.Done), "Completed issue count");
    AssertTrue(harness.State.Decisions.Count >= 3, "Loop should persist decisions.");
    AssertTrue(harness.State.AgentRuns.Any(run => !string.IsNullOrWhiteSpace(run.SessionId)),
        "Runs should record session ids.");
    AssertTrue(Directory.GetFiles(Path.Combine(harness.Store.WorkspacePath, "decisions"), "decision-*.md").Length > 0,
        "Decision artifacts should be written to the workspace.");
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
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
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
        AssertTrue(persistedJson.Contains("\"Roles\": [", StringComparison.Ordinal), "Hydrated workspace should be persisted back to disk.");
        AssertTrue(!persistedJson.Contains("\"Roles\": []", StringComparison.Ordinal), "Persisted workspace should no longer have empty role metadata.");
    }
    finally
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
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
            Directory.Delete(tempRoot, true);
        }
    }
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
            Directory.Delete(TempRoot, true);
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
