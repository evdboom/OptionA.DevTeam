using System.Diagnostics.CodeAnalysis;

using DevTeam.Cli;
using DevTeam.Core;
using DevTeam.TestInfrastructure;
using static DevTeam.SmokeTests.TestHelpers;

namespace DevTeam.SmokeTests;

[SuppressMessage("Major Code Smell", "S1192", Justification = "Smoke scenarios intentionally repeat literals for readability and traceability.")]
internal static class SmokeTestFunctions
{
    private static readonly string TestCliDllPath = Path.Combine("tools", "DevTeam.Cli.dll");
    private const string Context7ServerName = "context7";
    private const string AzureFoundryHost = "example.openai.azure.com";

    internal static void TestRunOnceBootstrapsAndQueues()
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
    
    internal static void TestCompleteRunUnlocksDependency()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
    
        var first = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, first.QueuedRuns[0].RunId, "completed", "Planning finished.");
        harness.Runtime.ApprovePlan(harness.State, "Approved the initial plan.");
        var second = harness.Runtime.RunOnce(harness.State, 2);
    
        AssertEqual("architect", second.QueuedRuns[0].RoleSlug, "Dependent role");
    }
    
    internal static void TestPlanningPhaseRequiresApproval()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
    
        var first = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, first.QueuedRuns[0].RunId, "completed", "Planning finished.");
        var blocked = harness.Runtime.RunOnce(harness.State, 1);
    
        AssertEqual("awaiting-plan-approval", blocked.State, "Loop state");
    }
    
    internal static void TestBlockingQuestionWaitState()
    {
        using var harness = new TestHarness();
        harness.Runtime.AddQuestion(harness.State, "Need production API key?", true);
    
        var result = harness.Runtime.RunOnce(harness.State, 2);
    
        AssertEqual("waiting-for-user", result.State, "Loop state");
    }
    
    internal static void TestPremiumCapFallback()
    {
        using var harness = new TestHarness();
        harness.State.Budget.PremiumCreditCap = 0;
        harness.Runtime.ApprovePlan(harness.State, "Use execution mode for reviewer work.");
        harness.Runtime.AddIssue(harness.State, "Review architecture", "", "reviewer", 100, null, []);
    
        var result = harness.Runtime.RunOnce(harness.State, 1);
    
        AssertEqual("gpt-5.4", result.QueuedRuns[0].ModelName, "Fallback model");
    }
    
    internal static void TestRoleSuggestedModelOverridesDefaultPolicy()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Use execution mode for frontend work.");
        harness.Runtime.AddIssue(harness.State, "Implement HUD", "", "frontend-developer", 100, null, []);
    
        var result = harness.Runtime.RunOnce(harness.State, 1);
    
        AssertEqual("claude-sonnet-4.6", result.QueuedRuns[0].ModelName, "Role suggested model should take precedence.");
    }
    
    internal static void TestCliAgentClientInvocationShape()
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
    
    internal static void TestSdkAgentClientFactory()
    {
        var client = AgentClientFactory.Create("sdk");
        AssertEqual("copilot-sdk", client.Name, "SDK backend name");
    }
    
    internal static void TestSdkCliPathResolverPrefersInstalledCopilot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    
        try
        {
            var executableName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
            var fakeCopilotPath = Path.Combine(tempRoot, executableName);
            File.WriteAllText(fakeCopilotPath, string.Empty);
    
            var resolvedPath = CopilotCliPathResolver.TryResolveFromPath(tempRoot);
    
            AssertEqual(fakeCopilotPath, resolvedPath, "PATH lookup should prefer an installed Copilot executable.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
    
    internal static void TestSdkCliPathResolverFailsWhenMissing()
    {
        try
        {
            CopilotCliPathResolver.Resolve(string.Empty);
            throw new InvalidOperationException("Missing PATH should have failed Copilot CLI resolution.");
        }
        catch (InvalidOperationException ex)
        {
            AssertTrue(ex.Message.Contains("available on PATH", StringComparison.Ordinal),
                "Missing PATH should produce a clear Copilot installation error.");
        }
    }
    
    internal static void TestToolUpdateCheckDetectsNewerStableVersions()
    {
        var status = ToolUpdateService.EvaluateVersions("0.1.18+abc123", ["0.1.17", "0.1.18", "0.1.19", "0.2.0-preview.1"]);
    
        AssertTrue(status.IsUpdateAvailable, "A newer stable package version should be detected.");
        AssertEqual("0.1.18", status.CurrentVersion, "Build metadata should be ignored in the installed version.");
        AssertEqual("0.1.19", status.LatestVersion, "Prerelease versions should not win over the newest stable release.");
    }
    
    internal static void TestToolUpdateCommandTargetsGlobalPackage()
    {
        var arguments = ToolUpdateService.BuildGlobalUpdateArguments("0.1.19");
    
        AssertTrue(arguments.SequenceEqual(["tool", "update", "--global", "OptionA.DevTeam", "--version", "0.1.19"]),
            "The update command should target the global OptionA.DevTeam tool package.");
    }
    
    internal static void TestBugReportCommandWritesIssueDraft()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-bug-report-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    
        try
        {
            var workspacePath = Path.Combine(tempRoot, ".devteam");
            var reportPath = Path.Combine(tempRoot, "bugreport.md");
            var initResult = RunDevTeamCli(tempRoot, "init", "--workspace", workspacePath, "--goal", "Build a Flappy bird game");
            AssertEqual(0, initResult.ExitCode, "Bug report init exit code");
    
            var result = RunDevTeamCli(tempRoot, "bug-report", "--workspace", workspacePath, "--save", reportPath);
    
            AssertEqual(0, result.ExitCode, "Bug report exit code");
            AssertTrue(result.StdOut.Contains("# DevTeam bug report draft", StringComparison.Ordinal), "Bug report output should include the draft heading.");
            AssertTrue(result.StdOut.Contains("DevTeam version:", StringComparison.Ordinal), "Bug report should include tool version.");
            AssertTrue(result.StdOut.Contains("Active goal: Build a Flappy bird game", StringComparison.Ordinal), "Bug report should include the active goal.");
            AssertTrue(File.Exists(reportPath), "Bug report command should save the draft when --save is used.");
        }
        finally
        {
            TryCleanupTempRepo(tempRoot);
        }
    }
    
    internal static void TestPlanCommandShowsArchitectSummaryWhenApprovalPending()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        var planning = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Initial plan ready.");
        harness.Runtime.ApprovePlan(harness.State, "Approved.");
    
        var architectIssue = harness.State.Issues.Single(issue =>
            !issue.IsPlanningIssue && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
        architectIssue.Status = ItemStatus.Done;
        harness.State.AgentRuns.Add(new AgentRun
        {
            Id = harness.State.NextRunId++,
            IssueId = architectIssue.Id,
            RoleSlug = "architect",
            ModelName = "claude-opus-4.6",
            Status = AgentRunStatus.Completed,
            Summary = "Use frontend-developer and tester issues for execution.",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        harness.Runtime.AddIssue(harness.State, "Implement canvas shell", "", "frontend-developer", 90, architectIssue.RoadmapItemId, []);
        harness.Runtime.AddIssue(harness.State, "Test gameplay loop", "", "tester", 80, architectIssue.RoadmapItemId, []);
        Directory.CreateDirectory(harness.Store.WorkspacePath);
        File.WriteAllText(Path.Combine(harness.Store.WorkspacePath, "plan.md"), "# Plan\n\nApproved.");
        harness.Store.Save(harness.State);
    
        var result = RunDevTeamCli(harness.RepoRoot, "plan", "--workspace", harness.Store.WorkspacePath);
    
        AssertEqual(0, result.ExitCode, "Plan command exit code");
        AssertTrue(result.StdOut.Contains("Architect Summary", StringComparison.Ordinal), "Plan should show the architect summary when architect approval is pending.");
        AssertTrue(result.StdOut.Contains("Execution issues created", StringComparison.Ordinal), "Plan should list execution issues when architect approval is pending.");
        AssertTrue(!result.StdOut.Contains("Run the loop to let the architect create execution issues", StringComparison.Ordinal),
            "Plan should not fall back to the generic architect-planning prompt when architect output already exists.");
    }
    
    internal static void TestPlanCommandShowsArchitectSummaryDuringArchitectPlanning()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        var planning = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Initial plan ready.");
        harness.Runtime.ApprovePlan(harness.State, "Approved.");
    
        var architectIssue = harness.State.Issues.Single(issue =>
            !issue.IsPlanningIssue && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
        architectIssue.Status = ItemStatus.Open;
        harness.State.AgentRuns.Add(new AgentRun
        {
            Id = harness.State.NextRunId++,
            IssueId = architectIssue.Id,
            RoleSlug = "architect",
            ModelName = "claude-opus-4.6",
            Status = AgentRunStatus.Completed,
            Summary = "Keep frontend-developer and tester roles in the execution plan.",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        harness.Runtime.AddIssue(harness.State, "Implement canvas shell", "", "frontend-developer", 90, architectIssue.RoadmapItemId, []);
        harness.Runtime.AddIssue(harness.State, "Test gameplay loop", "", "tester", 80, architectIssue.RoadmapItemId, []);
        Directory.CreateDirectory(harness.Store.WorkspacePath);
        File.WriteAllText(Path.Combine(harness.Store.WorkspacePath, "plan.md"), "# Plan\n\nApproved.");
        harness.Store.Save(harness.State);
    
        var result = RunDevTeamCli(harness.RepoRoot, "plan", "--workspace", harness.Store.WorkspacePath);
    
        AssertEqual(0, result.ExitCode, "Plan command exit code");
        AssertTrue(result.StdOut.Contains("Architect Summary", StringComparison.Ordinal),
            "Plan should show the latest architect summary throughout architect planning when output already exists.");
        AssertTrue(result.StdOut.Contains("Execution issues created", StringComparison.Ordinal),
            "Plan should continue listing the architect-created execution issues.");
        AssertTrue(!result.StdOut.Contains("Run the loop to let the architect create execution issues", StringComparison.Ordinal),
            "Plan should not hide the existing architect plan behind the generic architect-planning prompt.");
    }
    
    internal static void TestSdkSessionConfigWiresWorkspaceMcp()
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
            ToolHostPath = TestCliDllPath
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
    
    internal static void TestSdkSessionConfigWiresExternalMcpServers()
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
                    Name = Context7ServerName,
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
        AssertTrue(mcpServers.ContainsKey(Context7ServerName), "Context7 MCP server should be registered.");
        AssertTrue(!mcpServers.ContainsKey("disabled-server"), "Disabled MCP server should not be registered.");
        var context7 = mcpServers[Context7ServerName] as GitHub.Copilot.SDK.McpLocalServerConfig;
        AssertTrue(context7 is not null, "Context7 config should be a local MCP server.");
        AssertEqual("npx", context7!.Command, "Context7 should launch via npx.");
        AssertTrue(context7.Args.Contains("@upstash/context7-mcp@latest"), "Context7 should reference the correct package.");
    }

    internal static void TestSdkSessionConfigWiresByokProvider()
    {
        using var harness = new TestHarness();
        const string providerEnvVar = "DEVTEAM_TEST_PROVIDER_KEY";
        var originalValue = Environment.GetEnvironmentVariable(providerEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(providerEnvVar, "test-key");
            var request = new AgentInvocationRequest
            {
                Prompt = "Summarize the work.",
                Model = "gpt-5.4",
                SessionId = "provider-run-001",
                WorkingDirectory = harness.RepoRoot,
                Provider = new ProviderDefinition
                {
                    Name = "azure-foundry",
                    Type = "azure",
                    BaseUrl = $"https://{AzureFoundryHost}/openai",
                    ApiKeyEnvVar = providerEnvVar,
                    WireApi = "responses",
                    AzureApiVersion = "2024-10-21"
                }
            };

            var sessionConfig = WorkspaceMcpSessionConfigFactory.BuildSessionConfig(request);
            var provider = sessionConfig.Provider ?? throw new InvalidOperationException("Session config should include a provider.");

            AssertEqual("azure", provider.Type, "Provider type should map into the SDK session config.");
            AssertEqual($"https://{AzureFoundryHost}/openai", provider.BaseUrl, "Provider base URL should be preserved.");
            AssertEqual("test-key", provider.ApiKey, "Provider API key should be resolved from the environment.");
            AssertEqual("responses", provider.WireApi, "Provider wire API should be preserved.");
            AssertEqual("2024-10-21", provider.Azure?.ApiVersion, "Azure provider should carry its API version.");
            AssertEqual(false, sessionConfig.EnableConfigDiscovery, "BYOK session config should disable config discovery.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(providerEnvVar, originalValue);
        }
    }
    
    internal static void TestWorkspaceLoadsMcpServers()
    {
        using var harness = new TestHarness();
    
        AssertTrue(harness.State.McpServers.Count >= 1, "Workspace should load MCP server definitions from MCP_SERVERS.json.");
        AssertTrue(harness.State.McpServers.Any(s => s.Name == "context7"), "Context7 MCP server should be loaded.");
        var context7 = harness.State.McpServers.First(s => s.Name == "context7");
        AssertEqual("npx", context7.Command, "Context7 command should be npx.");
        AssertTrue(context7.Enabled, "Context7 should be enabled by default.");
    }
    
    internal static void TestWorkspaceLoadsModes()
    {
        using var harness = new TestHarness();
    
        AssertTrue(harness.State.Modes.Count >= 3, "Workspace should load at least the packaged modes.");
        AssertTrue(harness.State.Modes.Any(mode => mode.Slug == "develop"), "Develop mode should be available.");
        AssertTrue(harness.State.Modes.Any(mode => mode.Slug == "creative-writing"), "Creative writing mode should be available.");
        AssertTrue(harness.State.Modes.Any(mode => mode.Slug == "github"), "GitHub mode should be available.");
        AssertEqual("develop", harness.State.Runtime.ActiveModeSlug, "Develop should be the default active mode.");
    }
    
    internal static void TestPlanningSessionIsReusedAcrossPlanningRetries()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a reusable planning workflow.");
        harness.Store.Save(harness.State);
        var agent = new RecordingAgentClient(
            "OUTCOME: completed\nSUMMARY:\nInitial planning pass complete.",
            "OUTCOME: completed\nSUMMARY:\nPlanning updated after feedback.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
    
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
    
    internal static void TestIssueRetriesReuseTheSameSession()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Implement core loop", "Build the first pass.", "developer", 100, null, []);
        harness.Store.Save(harness.State);
        var agent = new RecordingAgentClient(
            "OUTCOME: failed\nSUMMARY:\nNeed another pass.",
            "OUTCOME: completed\nSUMMARY:\nCompleted on retry.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
    
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
    
    internal static void TestParallelPipelinesKeepIsolatedSessions()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Plan gameplay", "", "architect", 100, null, [], "gameplay");
        harness.Runtime.AddIssue(harness.State, "Plan menus", "", "architect", 95, null, [], "menus");
        harness.Store.Save(harness.State);
        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nPlanned.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
    
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
    
    internal static void TestPlanWorkflowGeneratesPlanWhenMissing()
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
    
    internal static void TestPlanWorkflowBlocksRunBeforePlanning()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a plan-first shell.");
        harness.Store.Save(harness.State);
    
        AssertTrue(PlanWorkflow.RequiresPlanningBeforeRun(harness.State, harness.Store), "Run commands should be blocked until a plan exists.");
    
        File.WriteAllText(PlanWorkflow.GetPlanPath(harness.Store), "# Plan\n\nReady for review.");
    
        AssertTrue(!PlanWorkflow.RequiresPlanningBeforeRun(harness.State, harness.Store), "Run commands should stop blocking once a plan exists.");
    }
    
    internal static void TestSetModeUpdatesRuntimeConfiguration()
    {
        using var harness = new TestHarness();
    
        harness.Runtime.SetMode(harness.State, "creative-writing");
    
        AssertEqual("creative-writing", harness.State.Runtime.ActiveModeSlug, "Mode should update.");
        AssertTrue(harness.State.Runtime.DefaultPipelineRoles.SequenceEqual(["architect", "developer", "reviewer"]),
            "Creative writing mode should swap the default pipeline roles.");
    }

    internal static void TestGitHubModeSyncImportsQueue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-github-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var workspacePath = Path.Combine(tempRoot, ".devteam");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalGitHubCliPath = Environment.GetEnvironmentVariable("DEVTEAM_GH_PATH");
        try
        {
            var ghScriptPath = Path.Combine(tempRoot, OperatingSystem.IsWindows() ? "gh.cmd" : "gh");
            File.WriteAllText(ghScriptPath, OperatingSystem.IsWindows() ? """
            @echo off
            if "%1 %2"=="auth status" exit /b 0
            if "%1 %2"=="issue list" (
            echo [{"number":101,"title":"Review queue import","body":"---\nrole: reviewer\npriority: 90\narea: repo sync\n---\nReview the imported GitHub issue.","labels":[{"name":"devteam:ready"}]},{"number":102,"title":"Clarify the release workflow","body":"Please confirm the release checklist.","labels":[{"name":"devteam:question"},{"name":"devteam:blocking"}]}]
            exit /b 0
            )
            echo Unexpected gh arguments 1>&2
            exit /b 1
            """ : "#!/usr/bin/env sh\n"
            + "set -eu\n"
            + "if [ \"${1:-} ${2:-}\" = \"auth status\" ]; then\n"
            + "  exit 0\n"
            + "fi\n"
            + "if [ \"${1:-} ${2:-}\" = \"issue list\" ]; then\n"
            + "  cat <<'JSON'\n"
            + "[{\"number\":101,\"title\":\"Review queue import\",\"body\":\"---\\nrole: reviewer\\npriority: 90\\narea: repo sync\\n---\\nReview the imported GitHub issue.\",\"labels\":[{\"name\":\"devteam:ready\"}]},{\"number\":102,\"title\":\"Clarify the release workflow\",\"body\":\"Please confirm the release checklist.\",\"labels\":[{\"name\":\"devteam:question\"},{\"name\":\"devteam:blocking\"}]}]\n"
            + "JSON\n"
            + "  exit 0\n"
            + "fi\n"
            + "echo \"Unexpected gh arguments: $*\" 1>&2\n"
            + "exit 1\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ghScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var separator = Path.PathSeparator;
            var mergedPath = string.IsNullOrWhiteSpace(originalPath)
                ? tempRoot
                : tempRoot + separator + originalPath;
            Environment.SetEnvironmentVariable("PATH", mergedPath);
            Environment.SetEnvironmentVariable("DEVTEAM_GH_PATH", ghScriptPath);

            var initResult = RunDevTeamCli(tempRoot, "init", "--workspace", workspacePath, "--mode", "github", "--recon", "false");
            AssertEqual(0, initResult.ExitCode, "GitHub mode init exit code");

            var syncResult = RunDevTeamCli(tempRoot, "github-sync", "--workspace", workspacePath);
            AssertEqual(
                0,
                syncResult.ExitCode,
                $"GitHub sync exit code (stdout: {syncResult.StdOut.Trim()} | stderr: {syncResult.StdErr.Trim()})");
            AssertTrue(syncResult.StdOut.Contains("GitHub sync complete:", StringComparison.Ordinal), "GitHub sync should print a summary.");

            var store = new WorkspaceStore(workspacePath);
            var state = store.Load();
            AssertEqual("github", state.Runtime.ActiveModeSlug, "GitHub mode should be active.");
            AssertTrue(state.Runtime.DefaultPipelineRoles.SequenceEqual(["developer", "reviewer"]),
                "GitHub mode should prefer the developer -> reviewer pipeline.");
            AssertTrue(state.Issues.Any(issue =>
                    issue.ExternalReference == "github#101"
                    && issue.RoleSlug == "reviewer"
                    && issue.Priority == 90
                    && issue.Area == "repo-sync"),
                "Ready GitHub issues should import into the local issue queue.");
            AssertTrue(state.Questions.Any(question =>
                    question.ExternalReference == "github#102"
                    && question.IsBlocking
                    && question.Text.Contains("Clarify the release workflow", StringComparison.Ordinal)),
                "Question-labelled GitHub issues should import as local questions.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEVTEAM_GH_PATH", originalGitHubCliPath);
            Environment.SetEnvironmentVariable("PATH", originalPath);
            TryCleanupTempRepo(tempRoot);
        }
    }

    internal static void TestSetProviderCommandUpdatesRuntimeConfiguration()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-provider-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var workspacePath = Path.Combine(tempRoot, ".devteam");
            var assetDir = Path.Combine(tempRoot, ".devteam-source");
            Directory.CreateDirectory(assetDir);
            File.WriteAllText(Path.Combine(assetDir, "PROVIDERS.json"), """
                [
                  {
                    "Name": "ollama-local",
                    "Type": "openai",
                    "BaseUrl": "http://localhost:11434/v1",
                    "ApiKeyEnvVar": "OLLAMA_API_KEY"
                  }
                ]
                """);

            var initResult = RunDevTeamCli(tempRoot, "init", "--workspace", workspacePath, "--goal", "Test provider defaults.", "--recon", "false");
            AssertEqual(0, initResult.ExitCode, "Provider init exit code");

            var setResult = RunDevTeamCli(tempRoot, "set-provider", "ollama-local", "--workspace", workspacePath);
            AssertEqual(0, setResult.ExitCode, "Set provider exit code");

            var providerResult = RunDevTeamCli(tempRoot, "provider", "--workspace", workspacePath);
            AssertEqual(0, providerResult.ExitCode, "Provider status exit code");
            AssertTrue(providerResult.StdOut.Contains("ollama-local", StringComparison.Ordinal), "Provider command should show the configured provider.");

            var state = new WorkspaceStore(workspacePath).Load();
            AssertEqual("ollama-local", state.Runtime.DefaultProviderName, "Workspace should persist the default provider override.");
        }
        finally
        {
            TryCleanupTempRepo(tempRoot);
        }
    }

    internal static void TestCustomizedPipelineSurvivesModeSwitch()
    {
        using var harness = new TestHarness();

        harness.Runtime.SetDefaultPipelineRoles(harness.State, ["architect", "developer", "reviewer"]);
        harness.Runtime.SetMode(harness.State, "autopilot");

        AssertEqual("autopilot", harness.State.Runtime.ActiveModeSlug, "Mode should still update.");
        AssertTrue(harness.State.Runtime.AutoApproveEnabled, "Autopilot should still enable auto-approve.");
        AssertTrue(harness.State.Runtime.PipelineRolesCustomized, "Custom pipeline flag should remain set.");
        AssertTrue(harness.State.Runtime.DefaultPipelineRoles.SequenceEqual(["architect", "developer", "reviewer"]),
            "Mode changes should not overwrite customized pipeline roles.");
    }
    
    internal static void TestSetKeepAwakeUpdatesRuntimeConfiguration()
    {
        using var harness = new TestHarness();
    
        AssertTrue(!harness.State.Runtime.KeepAwakeEnabled, "Keep-awake should be disabled by default.");
        harness.Runtime.SetKeepAwake(harness.State, true);
    
        AssertTrue(harness.State.Runtime.KeepAwakeEnabled, "Keep-awake should be enabled after the runtime update.");
    }
    
    internal static void TestGoalInputResolverLoadsMarkdownFromFile()
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
    
    internal static void TestPromptAssetsPreferDevTeamSource()
    {
        using var harness = new TestHarness();
        var role = harness.State.Roles.FirstOrDefault(item => item.Slug == "developer")
            ?? throw new InvalidOperationException("Developer role should be present.");
        var skill = harness.State.Skills.FirstOrDefault(item => item.Slug == "verify")
            ?? throw new InvalidOperationException("Verify skill should be present.");
    
        AssertTrue(role.SourcePath.StartsWith(".devteam-source", StringComparison.OrdinalIgnoreCase),
            "Roles should load from .devteam-source when present.");
        AssertTrue(skill.SourcePath.StartsWith(".devteam-source", StringComparison.OrdinalIgnoreCase),
            "Skills should load from .devteam-source when present.");
    }
    
    internal static void TestRunLoopExecutesWork()
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
            new FuncAgentClientFactory(_ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nCompleted the assigned task.")));
    
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
    
    internal static void TestExecutionLoopUsesOrchestratorSelectedBatch()
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
    SKILLS_USED:
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
    SKILLS_USED:
    (none)
    TOOLS_USED:
    (none)
    QUESTIONS:
    (none)
    """);
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => client));
    
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
    
    internal static void TestRunLoopResumesQueuedRuns()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
        var queued = harness.Runtime.RunOnce(harness.State, 1);
        AssertEqual("queued", queued.State, "Initial queue state");
        harness.Store.Save(harness.State);
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nPlanning finished.")));
    
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
    
    internal static void TestRunLoopPersistsQuestions()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a multi-stage autonomous dev-team loop.");
        var queued = harness.Runtime.RunOnce(harness.State, 1);
        AssertEqual("queued", queued.State, "Initial queue state");
        harness.Store.Save(harness.State);
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("""
    OUTCOME: blocked
    SUMMARY:
    Need a decision before continuing.
    QUESTIONS:
    - [blocking] Should the game use pixel art or vector art?
    """)));
    
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
    
    internal static void TestPlanningRunWritesPlanArtifact()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        var queued = harness.Runtime.RunOnce(harness.State, 1);
        AssertEqual("queued", queued.State, "Initial queue state");
        harness.Store.Save(harness.State);
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("""
    OUTCOME: completed
    SUMMARY:
    Build a small HTML5 Canvas game first, then add physics, obstacles, collision handling, score tracking, and playtesting.
    QUESTIONS:
    (none)
    """)));
    
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
    
    internal static void TestInitClearsLegacyArtifacts()
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
    
    internal static void TestPlanningFeedbackReopensPlanning()
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
    
    internal static void TestArchitectFeedbackReopensArchitectIssue()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        var planning = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Initial plan ready.");
        harness.Runtime.ApprovePlan(harness.State, "Approved.");
    
        var architectIssue = harness.State.Issues.Single(issue =>
            !issue.IsPlanningIssue && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
        architectIssue.Status = ItemStatus.Done;
    
        harness.Runtime.RecordPlanningFeedback(harness.State, "We need different execution roles.");
    
        AssertEqual(ItemStatus.Open, architectIssue.Status, "Architect feedback should reopen the architect issue");
        AssertTrue(harness.State.Decisions.Any(item => item.Source == "architect-plan-feedback"),
            "Architect feedback should be persisted as an architect-plan decision.");
    }
    
    internal static void TestArchitectFeedbackQueuesArchitectRerun()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        var planning = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Initial plan ready.");
        harness.Runtime.ApprovePlan(harness.State, "Approved.");
    
        var architectIssue = harness.State.Issues.Single(issue =>
            !issue.IsPlanningIssue && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
        architectIssue.Status = ItemStatus.Done;
    
        harness.Runtime.RecordPlanningFeedback(harness.State, "Use different execution roles.");
    
        var rerun = harness.Runtime.RunOnce(harness.State, 1);
    
        AssertEqual("queued", rerun.State, "Architect feedback should make architect work queue again");
        AssertEqual(1, rerun.QueuedRuns.Count, "Architect feedback should queue one architect rerun");
        AssertEqual("architect", rerun.QueuedRuns[0].RoleSlug, "Architect feedback should rerun the architect stage");
        AssertEqual(architectIssue.Id, rerun.QueuedRuns[0].IssueId, "Architect feedback should rerun the existing architect issue");
    }
    
    internal static void TestArchitectRerunHealsStaleReopenedPlanningIssue()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        var planning = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, planning.QueuedRuns[0].RunId, "completed", "Initial plan ready.");
        harness.Runtime.ApprovePlan(harness.State, "Approved.");
    
        var planningIssue = harness.State.Issues.Single(issue => issue.IsPlanningIssue);
        var architectIssue = harness.State.Issues.Single(issue =>
            !issue.IsPlanningIssue && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase));
    
        planningIssue.Status = ItemStatus.Open; // Simulate state left behind by the older bug.
        architectIssue.Status = ItemStatus.Done;
    
        harness.Runtime.RecordPlanningFeedback(harness.State, "Revise the architect plan.");
    
        var rerun = harness.Runtime.RunOnce(harness.State, 1);
    
        AssertEqual(ItemStatus.Done, planningIssue.Status, "Architect rerun should heal stale reopened planning issues.");
        AssertEqual("queued", rerun.State, "Architect rerun should still queue after healing stale planning state.");
        AssertEqual("architect", rerun.QueuedRuns[0].RoleSlug, "Architect rerun should select the architect issue after healing.");
    }
    
    internal static void TestAgentGeneratedIssues()
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
            new FuncAgentClientFactory(_ => new FakeAgentClient("""
    OUTCOME: completed
    SUMMARY:
    Architecture is defined. Implementation can proceed in small steps.
    ISSUES:
    - role=frontend-developer; priority=95; depends=none; title=Create HTML5 Canvas game scaffold; detail=Create the scaffold and render loop.
    QUESTIONS:
    (none)
    """)));
    
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
    
    internal static void TestIssueBoardMirror()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Build a flappy bird game.");
        harness.Runtime.AddIssue(
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
    
    internal static void TestGeneratedIssueRoleNormalization()
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
    
    internal static void TestRoleAliasesExposed()
    {
        using var harness = new TestHarness();
    
        var aliases = harness.Runtime.GetKnownRoleAliases(harness.State);
        var knownRoles = DevTeamRuntime.GetKnownRoleSlugs(harness.State);
        var exactResolved = harness.Runtime.TryResolveRoleSlug(harness.State, "developer", out var exactRole);
        var aliasResolved = harness.Runtime.TryResolveRoleSlug(harness.State, "engineer", out var aliasRole);
    
        AssertTrue(knownRoles.Contains("developer"), "Known roles should include developer.");
        AssertEqual("developer", aliases["engineer"], "Engineer alias should map to developer.");
        AssertTrue(exactResolved, "Exact roles should validate successfully.");
        AssertEqual("developer", exactRole, "Exact role should stay canonical.");
        AssertTrue(!aliasResolved, "Alias should not count as canonical validation success.");
        AssertEqual("developer", aliasRole, "Alias should resolve to canonical role.");
    }
    
    internal static void TestParallelLoopExecutesIndependentAreas()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Build render loop", "Implement the frame loop.", "developer", 90, null, [], "rendering");
        harness.Runtime.AddIssue(harness.State, "Add score tests", "Test score transitions.", "tester", 85, null, [], "testing");
    
        var agent = new FakeConcurrentAgentClient("OUTCOME: completed\nSUMMARY:\nCompleted the assigned task.");
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => agent));
    
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
    
    internal static void TestConflictPreventionAvoidsSameAreaRuns()
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
    
    internal static void TestArchitectPipelineCompletionCreatesDeveloperFollowUp()
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
    
    internal static void TestDeveloperPipelineCompletionCreatesTesterFollowUp()
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
    
    internal static void TestArchitectWorkGatesNonArchitectExecution()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Architecture pass", "", "architect", 80, null, [], "shared");
        harness.Runtime.AddIssue(harness.State, "Frontend implementation", "", "frontend-developer", 100, null, [], "ui");
    
        var result = harness.Runtime.RunOnce(harness.State, 2);
    
        AssertEqual(1, result.QueuedRuns.Count, "Architect gating should reduce the batch to architect work.");
        AssertEqual("architect", result.QueuedRuns.Single().RoleSlug, "Architect work should run before non-architect execution.");
    }
    
    internal static void TestPriorityGapReducesPipelineConcurrency()
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
    
    internal static void TestModeGuardrailsAppearInPrompt()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Build gameplay loop", "Create a playable gameplay loop.", "developer", 100, null, []);
        harness.Store.Save(harness.State);
        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDone.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
    
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
    
    internal static void TestPipelineHandoffAppearsInPrompt()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        var architectIssue = harness.Runtime.AddIssue(harness.State, "Plan gameplay loop", "Define the gameplay architecture.", "architect", 100, null, [], "gameplay");
        var queued = harness.Runtime.RunOnce(harness.State, 1);
        harness.Runtime.CompleteRun(harness.State, queued.QueuedRuns.Single().RunId, "completed", "Use a simple game loop, obstacle spawner, and a shared collision model.");
        harness.Store.Save(harness.State);
        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nImplemented the gameplay loop.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
    
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
    
    internal static void TestCollapsedResponseHeadersParseCleanly()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Scaffold app", "Create the project scaffold.", "developer", 90, null, []);
        harness.Store.Save(harness.State);
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("OUTCOME: completedSUMMARY:\nScaffolded the app.\nISSUES:\n(none)\nQUESTIONS:\n(none)")));
    
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
    
    internal static void TestRunArtifactsCaptureUsageMetadata()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Implement game loop", "Build the loop.", "developer", 90, null, []);
        harness.Store.Save(harness.State);
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("""
    OUTCOME: completed
    SUMMARY:
    Implemented the game loop.
    ISSUES:
    (none)
    SKILLS_USED:
    - plan
    - verify
    TOOLS_USED:
    - dotnet
    - node
    QUESTIONS:
    (none)
    """)));
    
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
    
        AssertTrue(run.SkillsUsed.SequenceEqual(["plan", "verify"]), "Run should capture used skills.");
        AssertTrue(run.ToolsUsed.SequenceEqual(["dotnet", "node"]), "Run should capture used tools.");
        AssertTrue(runArtifact.Contains("## Skills Used", StringComparison.Ordinal), "Run artifact should include skills.");
        AssertTrue(runArtifact.Contains("- plan", StringComparison.Ordinal), "Run artifact should list used skills.");
        AssertTrue(runArtifact.Contains("## Tools Used", StringComparison.Ordinal), "Run artifact should include tools.");
        AssertTrue(runArtifact.Contains("## Usage", StringComparison.Ordinal), "Run artifact should include usage telemetry.");
        AssertTrue(runArtifact.Contains("Committed credits: 1", StringComparison.Ordinal), "Run artifact should include committed credits.");
        AssertTrue(runArtifact.Contains("Tokens: unavailable from backend", StringComparison.Ordinal), "Run artifact should explain when token telemetry is unavailable.");
        AssertTrue(issueArtifact.Contains("Skills Used: plan, verify", StringComparison.Ordinal), "Issue mirror should include skill usage.");
        AssertTrue(issueArtifact.Contains("Tools Used: dotnet, node", StringComparison.Ordinal), "Issue mirror should include tool usage.");
    }

    internal static void TestStatusCommandShowsRoleUsage()
    {
        using var harness = new TestHarness();
        harness.State.AgentRuns.Add(new AgentRun
        {
            Id = 1,
            IssueId = 7,
            RoleSlug = "developer",
            Status = AgentRunStatus.Completed,
            CreditsUsed = 2,
            InputTokens = 1200,
            OutputTokens = 300,
            EstimatedCostUsd = 0.12
        });
        harness.Store.Save(harness.State);

        var result = RunDevTeamCli(harness.RepoRoot, "status", "--workspace", harness.Store.WorkspacePath);

        AssertEqual(0, result.ExitCode, "status exit code");
        AssertTrue(result.StdOut.Contains("Role usage:", StringComparison.Ordinal), "status should show the role usage section.");
        AssertTrue(result.StdOut.Contains("developer", StringComparison.Ordinal), "status should include the role slug.");
        AssertTrue(result.StdOut.Contains("2 credits", StringComparison.Ordinal), "status should include per-role credits.");
        AssertTrue(result.StdOut.Contains("1500 tokens", StringComparison.Ordinal), "status should include per-role token totals when available.");
    }

    internal static void TestBrownfieldLogCapturesApproachAndRationale()
    {
        using var harness = new TestHarness();
        harness.State.CodebaseContext = "## Existing patterns\n- MVC controllers";
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        harness.Runtime.AddIssue(harness.State, "Extend billing controller", "Add a new endpoint.", "developer", 90, null, []);
        harness.Store.Save(harness.State);
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("""
    OUTCOME: completed
    SUMMARY:
    Added the endpoint without changing the overall controller structure.
    APPROACH: extend
    RATIONALE:
    The current MVC controller pattern already matches the billing area and keeps the change local.
    ISSUES:
    (none)
    SKILLS_USED:
    (none)
    TOOLS_USED:
    - dotnet
    QUESTIONS:
    (none)
    """)));

        executor.RunAsync(
            harness.State,
            new LoopExecutionOptions
            {
                Backend = "sdk",
                MaxIterations = 1,
                MaxSubagents = 1,
                Verbosity = LoopVerbosity.Normal
            }).GetAwaiter().GetResult();

        var logPath = Path.Combine(harness.Store.WorkspacePath, "brownfield-delta.md");
        var logText = File.ReadAllText(logPath);
        var commandResult = RunDevTeamCli(harness.RepoRoot, "brownfield-log", "--workspace", harness.Store.WorkspacePath);

        AssertTrue(File.Exists(logPath), "Brownfield runs should write the brownfield delta log.");
        AssertTrue(logText.Contains("Approach: extend", StringComparison.Ordinal), "Brownfield log should capture the chosen approach.");
        AssertTrue(logText.Contains("controller pattern", StringComparison.Ordinal), "Brownfield log should capture the rationale.");
        AssertEqual(0, commandResult.ExitCode, "brownfield-log exit code");
        AssertTrue(commandResult.StdOut.Contains("Brownfield Change Delta", StringComparison.Ordinal), "brownfield-log should print the audit log.");
    }
    
    internal static void TestLegacyWorkspaceHydratesMetadata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var repoRoot = TestHarness.FindRepoRootForTests();
            var store = new WorkspaceStore(Path.Combine(tempRoot, ".devteam"));
            var state = store.Initialize(repoRoot, 25, 6);
            state.Roles = [];
            state.Skills = [];
            File.WriteAllText(
                store.StatePath,
                System.Text.Json.JsonSerializer.Serialize(
                    state,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    
            var loaded = store.Load();
            var persistedJson = File.ReadAllText(store.StatePath);
    
            AssertTrue(loaded.Roles.Count > 0, "Legacy workspace should rehydrate roles on load.");
            AssertTrue(loaded.Skills.Count > 0, "Legacy workspace should rehydrate skills on load.");
            AssertTrue(persistedJson.Contains("\"FormatVersion\": 4", StringComparison.Ordinal), "Hydrated workspace should be migrated to the current manifest format.");
            AssertTrue(!File.Exists(Path.Combine(store.StateDirectoryPath, "roles.json")), "Derived role assets should not be persisted into state files.");
        }
        finally
        {
            TryCleanupTempRepo(tempRoot);
        }
    }
    
    internal static void TestWorkspaceLoadsLegacyExecutionSelectionTimestamp()
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
    
    internal static void TestFriendlyRoleNamesResolve()
    {
        using var harness = new TestHarness();
    
        var resolved = harness.Runtime.TryResolveRoleSlug(harness.State, "Front-end developer", out var role);
    
        AssertTrue(resolved, "Friendly role name should resolve successfully.");
        AssertEqual("frontend-developer", role, "Friendly role name should map to canonical slug.");
    }
    
    internal static void TestExternalReposFallBackToPackagedAssets()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-tests", Guid.NewGuid().ToString("N"));
        var repoRoot = Path.Combine(tempRoot, "target-repo");
        Directory.CreateDirectory(repoRoot);
        try
        {
            var store = new WorkspaceStore(Path.Combine(repoRoot, ".devteam"));
            var state = store.Initialize(repoRoot, 25, 6);
    
            AssertTrue(state.Roles.Count > 0, "External repos without local .devteam-source should still load roles.");
            AssertTrue(state.Skills.Count > 0, "External repos without local .devteam-source should still load skills.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                TestFileSystem.DeleteDirectoryWithRetries(tempRoot);
            }
        }
    }
    
    internal static void TestGitHelperInitializesRepository()
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
    
    internal static void TestGitHelperStagesOnlyIterationChanges()
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
    
    internal static void TestLoopStagesChangedFilesAfterIteration()
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
                new FuncAgentClientFactory(_ => new FileWritingAgentClient("generated.txt", "OUTCOME: completed\nSUMMARY:\nDone.")));
    
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
    
    internal static void TestConcurrentWorkspaceSavesRemainParseable()
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
    
    internal static void TestWorkspaceManifestShardsCollections()
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
    
    internal static void TestWorkspaceSaveToleratesReplaceBlockingReaders()
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
    
    internal static void TestPromptAssetsAreNotPersisted()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Keep persisted context small.");
        harness.Store.Save(harness.State);
    
        var manifestJson = File.ReadAllText(harness.Store.StatePath);
        var rolesPath = Path.Combine(harness.Store.StateDirectoryPath, "roles.json");
        var skillsPath = Path.Combine(harness.Store.StateDirectoryPath, "skills.json");
    
        AssertTrue(harness.State.Roles.Count > 0, "Roles should still be available in memory for prompt building.");
        AssertTrue(harness.State.Skills.Count > 0, "Skills should still be available in memory for prompt building.");
        AssertTrue(!manifestJson.Contains("## Suggested Model", StringComparison.Ordinal), "Manifest should not inline prompt markdown bodies.");
        AssertTrue(!File.Exists(rolesPath), "Roles should not be persisted into the state directory.");
        AssertTrue(!File.Exists(skillsPath), "Skills should not be persisted into the state directory.");
    }
    
    internal static void TestExecutionOrchestratorEmitsHeartbeat()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        var issue = harness.Runtime.AddIssue(harness.State, "Plan gameplay architecture", "", "architect", 100, null, [], "gameplay");
        harness.Store.Save(harness.State);
        var messages = new List<string>();
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeStaggeredAgentClient("""
    OUTCOME: completed
    SUMMARY:
    Run the architect batch first.
    SELECTED_ISSUES:
    - ISSUE_ID
    ISSUES:
    (none)
    SKILLS_USED:
    (none)
    TOOLS_USED:
    (none)
    QUESTIONS:
    (none)
    """.Replace("ISSUE_ID", issue.Id.ToString(), StringComparison.Ordinal), TimeSpan.FromMilliseconds(150), TimeSpan.Zero)));
    
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
    
    internal static void TestExistingPipelineFollowUpsAreNormalized()
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
    
    internal static void TestPlanApprovalTransitionsToArchitectPlanning()
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
    
    internal static void TestArchitectApprovalTransitionsToExecution()
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
    
    internal static void TestAutoApproveSkipsBothGates()
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
            new FuncAgentClientFactory(_ => new FakeAgentClient("OUTCOME: completed\nSUMMARY:\nDone.")));
    
        var messages = new List<string>();
        executor.RunAsync(
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
    
    internal static void TestAutopilotModeEnablesAutoApprove()
    {
        using var harness = new TestHarness();
        AssertTrue(!harness.State.Runtime.AutoApproveEnabled, "Auto-approve should be off by default.");
    
        harness.Runtime.SetMode(harness.State, "autopilot");
        AssertEqual("autopilot", harness.State.Runtime.ActiveModeSlug, "Active mode should be autopilot.");
        AssertTrue(harness.State.Runtime.AutoApproveEnabled, "Autopilot mode should enable auto-approve.");
    
        harness.Runtime.SetMode(harness.State, "develop");
        AssertTrue(!harness.State.Runtime.AutoApproveEnabled, "Switching away from autopilot should disable auto-approve.");
    }
    
    internal static void TestDesignOnlyRolesReceiveFileBoundary()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Approve.");
    
        // Architect should get file boundary enforcement
        harness.Runtime.AddIssue(harness.State, "Design the architecture", "Choose patterns.", "architect", 100, null, []);
        harness.Store.Save(harness.State);
        var agent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nDesigned.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
        executor.RunAsync(harness.State, new LoopExecutionOptions { Backend = "sdk", MaxIterations = 1, MaxSubagents = 1, Verbosity = LoopVerbosity.Normal }).GetAwaiter().GetResult();
        var architectPrompt = agent.LastPrompt ?? throw new InvalidOperationException("Expected architect prompt.");
        AssertTrue(architectPrompt.Contains("FILE BOUNDARY", StringComparison.Ordinal), "Architect prompt should include FILE BOUNDARY enforcement.");
        AssertTrue(architectPrompt.Contains("design-only role", StringComparison.Ordinal), "Architect prompt should state it is a design-only role.");
        AssertTrue(architectPrompt.Contains("constructor injection", StringComparison.OrdinalIgnoreCase), "Architect prompt should require constructor injection.");
        AssertTrue(architectPrompt.Contains("file system", StringComparison.OrdinalIgnoreCase)
            && architectPrompt.Contains("clock", StringComparison.OrdinalIgnoreCase),
            "Architect prompt should call out explicit infrastructure abstractions for testability.");

        var auditorIssue = harness.Runtime.AddIssue(harness.State, "Audit recent codebase drift", "Inspect recent maintainability erosion.", "auditor", 90, null, []);
        var auditorPrompt = AgentPromptBuilder.BuildPrompt(harness.State, auditorIssue);
        AssertTrue(auditorPrompt.Contains("FILE BOUNDARY", StringComparison.Ordinal), "Auditor prompt should include FILE BOUNDARY enforcement.");
        AssertTrue(auditorPrompt.Contains("legacy drift", StringComparison.OrdinalIgnoreCase)
            && auditorPrompt.Contains("recent drift", StringComparison.OrdinalIgnoreCase)
            && auditorPrompt.Contains("active regression risk", StringComparison.OrdinalIgnoreCase),
            "Auditor prompt should classify drift findings explicitly.");
        AssertTrue(auditorPrompt.Contains("Reviewer", StringComparison.Ordinal)
            && auditorPrompt.Contains("Navigator", StringComparison.Ordinal)
            && auditorPrompt.Contains("Security", StringComparison.Ordinal),
            "Auditor prompt should define boundaries against reviewer, navigator, and security.");
    
        // Developer should NOT get file boundary enforcement
        harness.Runtime.AddIssue(harness.State, "Build the game loop", "Implement it.", "developer", 80, null, []);
        harness.Store.Save(harness.State);
        var devAgent = new RecordingAgentClient("OUTCOME: completed\nSUMMARY:\nBuilt.");
        var devExecutor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => devAgent));
        devExecutor.RunAsync(harness.State, new LoopExecutionOptions { Backend = "sdk", MaxIterations = 1, MaxSubagents = 1, Verbosity = LoopVerbosity.Normal }).GetAwaiter().GetResult();
        var devPrompt = devAgent.LastPrompt ?? throw new InvalidOperationException("Expected developer prompt.");
        AssertTrue(!devPrompt.Contains("FILE BOUNDARY", StringComparison.Ordinal), "Developer prompt should NOT include FILE BOUNDARY enforcement.");
    }
    
    internal static void TestPlannerCannotCreateDuplicateArchitectIssues()
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
    
    internal static void TestInitRejectsMisspelledGoalOption()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    
        try
        {
            var workspacePath = Path.Combine(tempRoot, ".devteam");
            var result = RunDevTeamCli(tempRoot, "init", "--workspace", workspacePath, "--goall", "Build a Flappy bird game");
    
            AssertEqual(1, result.ExitCode, "Misspelled init option exit code");
            AssertTrue(result.StdErr.Contains("Unknown option '--goall'", StringComparison.Ordinal), "Misspelled init option should be reported.");
            AssertTrue(result.StdErr.Contains("--goal", StringComparison.Ordinal), "Misspelled init option should suggest --goal.");
            AssertTrue(!File.Exists(Path.Combine(workspacePath, "workspace.json")), "Init should not create workspace state when option validation fails.");
        }
        finally
        {
            TryCleanupTempRepo(tempRoot);
        }
    }

    internal static void TestEditIssueCommandUpdatesQueuedIssue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var workspacePath = Path.Combine(tempRoot, ".devteam");
            var initResult = RunDevTeamCli(tempRoot, "init", "--workspace", workspacePath);
            AssertEqual(0, initResult.ExitCode, "Init exit code");

            var addResult = RunDevTeamCli(
                tempRoot,
                "add-issue",
                "Draft UI flow",
                "--workspace", workspacePath,
                "--role", "developer",
                "--detail", "Initial scope.",
                "--priority", "40");
            AssertEqual(0, addResult.ExitCode, "Add issue exit code");

            var editResult = RunDevTeamCli(
                tempRoot,
                "edit-issue",
                "1",
                "--workspace", workspacePath,
                "--priority", "90",
                "--area", "UI Layer",
                "--status", "blocked",
                "--note", "Waiting on UX copy.");
            AssertEqual(0, editResult.ExitCode, "Edit issue exit code");

            var store = new WorkspaceStore(workspacePath);
            var state = store.Load();
            var issue = state.Issues.Single(item => item.Id == 1);
            AssertEqual(90, issue.Priority, "Edited issue priority");
            AssertEqual("ui-layer", issue.Area, "Edited issue area");
            AssertEqual(ItemStatus.Blocked, issue.Status, "Edited issue status");
            AssertTrue(issue.Notes.Contains("Waiting on UX copy.", StringComparison.Ordinal), "Edited issue should append notes.");
        }
        finally
        {
            TryCleanupTempRepo(tempRoot);
        }
    }

    internal static void TestSetPipelineCommandUpdatesDefaultRoles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "devteam-set-pipeline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var workspacePath = Path.Combine(tempRoot, ".devteam");
            var initResult = RunDevTeamCli(tempRoot, "init", "--workspace", workspacePath, "--goal", "Build an expert workflow.");
            AssertEqual(0, initResult.ExitCode, "Init exit code");

            var setResult = RunDevTeamCli(
                tempRoot,
                "set-pipeline",
                "architect",
                "developer",
                "reviewer",
                "--workspace", workspacePath);
            AssertEqual(0, setResult.ExitCode, "set-pipeline exit code");

            var pipelineResult = RunDevTeamCli(tempRoot, "pipeline", "--workspace", workspacePath);
            AssertEqual(0, pipelineResult.ExitCode, "pipeline exit code");
            AssertTrue(pipelineResult.StdOut.Contains("architect -> developer -> reviewer", StringComparison.Ordinal),
                "pipeline should print the customized role chain.");
            AssertTrue(pipelineResult.StdOut.Contains("[custom]", StringComparison.Ordinal),
                "pipeline should identify a customized role chain.");

            var store = new WorkspaceStore(workspacePath);
            var state = store.Load();
            AssertTrue(state.Runtime.PipelineRolesCustomized, "Custom pipeline flag should be saved.");
            AssertTrue(state.Runtime.DefaultPipelineRoles.SequenceEqual(["architect", "developer", "reviewer"]),
                "set-pipeline should persist the requested role chain.");
        }
        finally
        {
            TryCleanupTempRepo(tempRoot);
        }
    }

    internal static void TestDiffRunCommandShowsRunDelta()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        var issue = harness.Runtime.AddIssue(harness.State, "Implement traceable UI", "Build the UI slice.", "developer", 90, null, [], "ui");
        harness.State.Issues.Add(new IssueItem
        {
            Id = harness.State.NextIssueId++,
            Title = "Test traceable UI",
            RoleSlug = "tester",
            Area = "ui",
            Status = ItemStatus.Open
        });
        harness.State.Questions.Add(new QuestionItem
        {
            Id = harness.State.NextQuestionId++,
            Text = "Use light or dark theme?",
            IsBlocking = true,
            Status = QuestionStatus.Open
        });
        harness.State.AgentRuns.Add(new AgentRun
        {
            Id = 12,
            IssueId = issue.Id,
            RoleSlug = "developer",
            Status = AgentRunStatus.Completed,
            Summary = "Implemented the UI slice.",
            ResultingIssueStatus = ItemStatus.Done,
            ChangedPaths = ["src/Ui.cs", "tests/UiTests.cs"],
            CreatedIssueIds = [2],
            CreatedQuestionIds = [1]
        });
        harness.Store.Save(harness.State);

        var result = RunDevTeamCli(harness.RepoRoot, "diff-run", "12", "--workspace", harness.Store.WorkspacePath);

        AssertEqual(0, result.ExitCode, "diff-run exit code");
        AssertTrue(result.StdOut.Contains("Run #12 diff", StringComparison.Ordinal), "diff-run should identify the run.");
        AssertTrue(result.StdOut.Contains("src/Ui.cs", StringComparison.Ordinal), "diff-run should list changed files.");
        AssertTrue(result.StdOut.Contains("Test traceable UI", StringComparison.Ordinal), "diff-run should list created issues.");
        AssertTrue(result.StdOut.Contains("Use light or dark theme?", StringComparison.Ordinal), "diff-run should list created questions.");
    }

    internal static void TestWorkspaceExportImportRoundTrip()
    {
        using var harness = new TestHarness();
        harness.Runtime.SetGoal(harness.State, "Ship a portable workspace.");
        harness.Runtime.AddIssue(harness.State, "Implement export", "Package the workspace.", "developer", 80, null, [], "cli");
        harness.State.Questions.Add(new QuestionItem
        {
            Id = harness.State.NextQuestionId++,
            Text = "Should import overwrite existing files?",
            IsBlocking = true,
            Status = QuestionStatus.Open
        });
        harness.Store.Save(harness.State);

        var archivePath = Path.Combine(harness.TempRoot, "handoff.zip");
        var importedWorkspace = Path.Combine(harness.TempRoot, ".devteam-imported");

        var exportResult = RunDevTeamCli(harness.RepoRoot, "export", "--workspace", harness.Store.WorkspacePath, "--output", archivePath);
        AssertEqual(0, exportResult.ExitCode, "Export exit code");
        AssertTrue(File.Exists(archivePath), "Export should create the archive.");

        var importResult = RunDevTeamCli(harness.RepoRoot, "import", "--workspace", importedWorkspace, "--input", archivePath);
        AssertEqual(0, importResult.ExitCode, "Import exit code");

        var importedStore = new WorkspaceStore(importedWorkspace);
        var importedState = importedStore.Load();
        AssertEqual(harness.State.ActiveGoal?.GoalText, importedState.ActiveGoal?.GoalText, "Imported goal");
        AssertEqual(harness.State.Issues.Count, importedState.Issues.Count, "Imported issue count");
        AssertEqual(harness.State.Questions.Count, importedState.Questions.Count, "Imported question count");
        AssertTrue(importedState.Issues.Any(issue => issue.Title == "Implement export"), "Imported workspace should contain exported issue.");
    }

    internal static void TestArchitectRunUpdatesPlanArtifact()
    {
        using var harness = new TestHarness();
        // Start in Execution phase (no architect issues — ApprovePlan skips architect planning).
        harness.Runtime.ApprovePlan(harness.State, "Plan approved.");
        // Add a non-planning architect issue (a real execution-phase architect issue).
        harness.Runtime.AddIssue(harness.State, "Design data model", "Define the core domain entities.", "architect", 100, null, []);
        harness.Store.Save(harness.State);
    
        var executor = new LoopExecutor(
            harness.Runtime,
            harness.Store,
            new FuncAgentClientFactory(_ => new FakeAgentClient("""
                OUTCOME: completed
                SUMMARY:
                Use a flat ECS data model: Position, Velocity, Sprite components; PhysicsSystem, RenderSystem.
                QUESTIONS:
                (none)
                """)));
    
        executor.RunAsync(
            harness.State,
            new LoopExecutionOptions
            {
                Backend = "sdk",
                MaxIterations = 2,
                MaxSubagents = 1,
                Verbosity = LoopVerbosity.Normal
            }).GetAwaiter().GetResult();
    
        var planPath = Path.Combine(harness.Store.WorkspacePath, "plan.md");
        AssertTrue(File.Exists(planPath), "Architect run should write a plan.md artifact");
        var planContent = File.ReadAllText(planPath);
        AssertTrue(planContent.Contains("flat ECS data model", StringComparison.Ordinal),
            "plan.md should contain the architect summary");
        AssertTrue(planContent.Contains("Detailed execution plan", StringComparison.Ordinal),
            "plan.md should use the architect header, not the planner header");
    }
    
    internal static void TestConflictPreventionHoldsAtHighSubagentCount()
    {
        using var harness = new TestHarness();
        harness.Runtime.ApprovePlan(harness.State, "Run in execution mode.");
        // Two issues in the same area (only one can run at a time) + two in distinct areas.
        harness.Runtime.AddIssue(harness.State, "Build bird entity", "Implement bird state.", "developer", 90, null, [], "gameplay");
        harness.Runtime.AddIssue(harness.State, "Tune flap physics", "Adjust gravity.", "developer", 85, null, [], "gameplay");
        harness.Runtime.AddIssue(harness.State, "Build score UI", "Show score on screen.", "developer", 80, null, [], "ui");
        harness.Runtime.AddIssue(harness.State, "Add score tests", "Test scoring.", "tester", 75, null, [], "testing");
    
        var agent = new FakeConcurrentAgentClient("OUTCOME: completed\nSUMMARY:\nCompleted the task.");
        var executor = new LoopExecutor(harness.Runtime, harness.Store, new FuncAgentClientFactory(_ => agent));
    
        executor.RunAsync(
            harness.State,
            new LoopExecutionOptions
            {
                Backend = "sdk",
                MaxIterations = 1,
                MaxSubagents = 4,
                Verbosity = LoopVerbosity.Normal
            }).GetAwaiter().GetResult();
    
        // With 4 capacity but 2 same-area issues: expect 1 gameplay + 1 ui + 1 testing = 3 concurrent.
        AssertTrue(agent.MaxConcurrentInvocations <= 3,
            "Conflict prevention should cap same-area concurrent runs even at max-subagents=4.");
        AssertTrue(agent.MaxConcurrentInvocations >= 3,
            "Three independent areas should run concurrently when max-subagents=4.");
    }
    
}

