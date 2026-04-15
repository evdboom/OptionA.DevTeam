using DevTeam.Core;
using DevTeam.Cli.Shell;

namespace DevTeam.Cli;

/// <summary>
/// Launches the interactive shell with synthetic workspace data so the UI
/// can be exercised and visually inspected without real agent backends.
/// Invoked via: devteam ui-harness [--scenario NAME]
/// </summary>
internal static class UiHarness
{
    internal static async Task<int> RunAsync(Dictionary<string, List<string>> options)
    {
        var scenario = CliOptionParser.GetOption(options, "scenario") ?? "execution";
        var workspacePath = Path.Combine(Path.GetTempPath(), $"devteam-ui-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspacePath);

        try
        {
            var store = new WorkspaceStore(workspacePath);
            var state = BuildScenarioState(scenario, workspacePath);
            store.Save(state);

            var runtime = new DevTeamRuntime();
            var executor = new LoopExecutor(runtime, store);
            using var toolUpdateService = new ToolUpdateService();
            using var exitCts = new CancellationTokenSource();
            using var shell = new ShellService(
                store, runtime, executor, toolUpdateService,
                new ShellStartOptions(options), () => exitCts.Cancel());

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            await SpectreShellHost.RunAsync(shell, exitCts.Token);
            return 0;
        }
        finally
        {
            try { Directory.Delete(workspacePath, recursive: true); } catch { }
        }
    }

    private static WorkspaceState BuildScenarioState(string scenario, string workspacePath)
    {
        return scenario.ToLowerInvariant() switch
        {
            "empty" => BuildEmptyScenario(workspacePath),
            "planning" => BuildPlanningScenario(workspacePath),
            "architect" => BuildArchitectScenario(workspacePath),
            "execution" => BuildExecutionScenario(workspacePath),
            "questions" => BuildQuestionsScenario(workspacePath),
            _ => BuildExecutionScenario(workspacePath),
        };
    }

    private static WorkspaceState BuildBaseState(string workspacePath)
    {
        return new WorkspaceState
        {
            RepoRoot = workspacePath,
            Runtime = RuntimeConfiguration.CreateDefault(),
            Budget = new BudgetState
            {
                TotalCreditCap = 25,
                PremiumCreditCap = 6,
                CreditsCommitted = 4.5,
                PremiumCreditsCommitted = 1.2,
            },
            ActiveGoal = new GoalState
            {
                GoalText = "Build a real-time dashboard with WebSocket support",
            },
        };
    }

    internal static WorkspaceState BuildEmptyScenario(string workspacePath) =>
        BuildBaseState(workspacePath);

    internal static WorkspaceState BuildPlanningScenario(string workspacePath)
    {
        var state = BuildBaseState(workspacePath);
        state.Phase = WorkflowPhase.Planning;
        state.Issues.Add(new IssueItem
        {
            Id = 1, Title = "Plan real-time dashboard feature", RoleSlug = "planner",
            IsPlanningIssue = true, Status = ItemStatus.Done, Priority = 100
        });
        state.Questions.Add(new QuestionItem
        {
            Id = 1, Text = "Should we use SignalR or raw WebSockets for the real-time connection?",
            IsBlocking = true, Status = QuestionStatus.Open
        });
        return state;
    }

    internal static WorkspaceState BuildArchitectScenario(string workspacePath)
    {
        var state = BuildBaseState(workspacePath);
        state.Phase = WorkflowPhase.ArchitectPlanning;
        state.Issues.AddRange([
            new IssueItem { Id = 1, Title = "Plan dashboard", RoleSlug = "planner", IsPlanningIssue = true, Status = ItemStatus.Done, Priority = 100 },
            new IssueItem { Id = 2, Title = "Design dashboard architecture", RoleSlug = "architect", Status = ItemStatus.Done, Priority = 90 },
            new IssueItem { Id = 3, Title = "Implement SignalR hub", RoleSlug = "backend-developer", Status = ItemStatus.Open, Priority = 80 },
            new IssueItem { Id = 4, Title = "Create dashboard Blazor component", RoleSlug = "frontend-developer", Status = ItemStatus.Open, Priority = 70 },
            new IssueItem { Id = 5, Title = "Add WebSocket integration tests", RoleSlug = "tester", Status = ItemStatus.Open, Priority = 60, DependsOnIssueIds = [3] },
        ]);
        state.AgentRuns.Add(new AgentRun
        {
            Id = 1, IssueId = 2, RoleSlug = "architect", ModelName = "claude-opus-4.6",
            Status = AgentRunStatus.Completed, Summary = "Designed hub architecture with fan-out pattern."
        });
        return state;
    }

    internal static WorkspaceState BuildExecutionScenario(string workspacePath)
    {
        // Mirrors the real AOTfier workspace: 44 issues, 1 running agent,
        // many done/open items — exercises the roadmap scrolling and long lists.
        var state = BuildBaseState(workspacePath);
        state.Phase = WorkflowPhase.Execution;
        state.Budget.CreditsCommitted = 18.5;
        state.Budget.PremiumCreditsCommitted = 3.2;
        state.ActiveGoal = new GoalState { GoalText = "Build an AOT analysis tool for .NET projects" };
        state.Issues.AddRange([
            new IssueItem { Id = 1, Title = "Run the planning session and split the work", RoleSlug = "planner", IsPlanningIssue = true, Status = ItemStatus.Done, Priority = 100 },
            new IssueItem { Id = 2, Title = "Design the technical approach and create execution issues", RoleSlug = "architect", Status = ItemStatus.Done, Priority = 90 },
            new IssueItem { Id = 3, Title = "Write project README and rule catalogue", RoleSlug = "docs", Status = ItemStatus.Done, Priority = 80 },
            new IssueItem { Id = 5, Title = "Create solution structure and project scaffolding", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 75 },
            new IssueItem { Id = 6, Title = "Implement core models and rule interface", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 70 },
            new IssueItem { Id = 7, Title = "Implement solution analyzer engine", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 65 },
            new IssueItem { Id = 8, Title = "Implement reflection analysis rules", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 60 },
            new IssueItem { Id = 9, Title = "Implement dynamic loading, code generation, and serialization rules", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 55 },
            new IssueItem { Id = 10, Title = "Implement console and HTML report generators", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 50 },
            new IssueItem { Id = 11, Title = "Implement fix suggestion and application system", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 45 },
            new IssueItem { Id = 12, Title = "Implement CLI tool entry point and command wiring", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 40 },
            new IssueItem { Id = 15, Title = "Create solution and project scaffold for AOT tool", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 75 },
            new IssueItem { Id = 18, Title = "Align pre-added tests with engine implementation sequence", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 60 },
            new IssueItem { Id = 19, Title = "Scout codebase for: Implement core models and rule interface", RoleSlug = "navigator", Status = ItemStatus.Open, Priority = 70 },
            new IssueItem { Id = 20, Title = "Implement solution and project scaffold for AOT tool", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 75 },
            new IssueItem { Id = 23, Title = "Close or supersede issue #5 (scaffolding)", RoleSlug = "architect", Status = ItemStatus.Done, Priority = 80 },
            new IssueItem { Id = 24, Title = "Close or supersede issue #13 (implement technical approach)", RoleSlug = "architect", Status = ItemStatus.Done, Priority = 80 },
            new IssueItem { Id = 25, Title = "Triage engine issues #6–#12 against existing implementation", RoleSlug = "architect", Status = ItemStatus.Done, Priority = 78 },
            new IssueItem { Id = 26, Title = "Fix CoreModelsTests to match RuleBase Descriptor API", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 70 },
            new IssueItem { Id = 27, Title = "Implement core models and rule interface", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 70 },
            new IssueItem { Id = 28, Title = "Repair engine test contract drift after model and rule changes", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 68 },
            new IssueItem { Id = 31, Title = "Implement Close or supersede issue #5 (scaffolding)", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 75 },
            new IssueItem { Id = 33, Title = "Finish reflection rule coverage", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 60 },
            new IssueItem { Id = 34, Title = "Split expression compile rule and add non-reflection rules", RoleSlug = "developer", Status = ItemStatus.Done, Priority = 58 },
            new IssueItem { Id = 35, Title = "Close CLI option parity gaps", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 40 },
            new IssueItem { Id = 37, Title = "Close or verify issue #32 (Fix CoreModelsTests)", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 70 },
            new IssueItem { Id = 38, Title = "Drop stale navigator #19 (scout for #6)", RoleSlug = "architect", Status = ItemStatus.InProgress, Priority = 80 },
            new IssueItem { Id = 39, Title = "Test Close or supersede issue #5 (scaffolding)", RoleSlug = "tester", Status = ItemStatus.Open, Priority = 50 },
            new IssueItem { Id = 40, Title = "Implement Finish reflection rule coverage", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 60 },
            new IssueItem { Id = 41, Title = "Implement Repair engine test contract drift", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 58 },
            new IssueItem { Id = 42, Title = "Implement Split expression compile rule", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 55 },
            new IssueItem { Id = 43, Title = "Implement core models and rule interface (direct)", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 70 },
            new IssueItem { Id = 44, Title = "Finish reflection rule coverage", RoleSlug = "developer", Status = ItemStatus.Open, Priority = 60 },
        ]);
        state.AgentRuns.AddRange([
            new AgentRun { Id = 1, IssueId = 2, RoleSlug = "architect", ModelName = "claude-haiku-4.5", Status = AgentRunStatus.Completed, Summary = "Architecture designed." },
            new AgentRun { Id = 22, IssueId = 38, RoleSlug = "architect", ModelName = "claude-haiku-4.5", Status = AgentRunStatus.Running, SessionId = "ses-arch-38" },
        ]);
        return state;
    }

    internal static WorkspaceState BuildQuestionsScenario(string workspacePath)
    {
        var state = BuildExecutionScenario(workspacePath);
        state.Questions.AddRange([
            new QuestionItem { Id = 1, Text = "Should the dashboard support dark mode by default?", IsBlocking = false, Status = QuestionStatus.Open },
            new QuestionItem { Id = 2, Text = "What authentication provider should we use for WebSocket connections?", IsBlocking = true, Status = QuestionStatus.Open },
        ]);
        return state;
    }
}
