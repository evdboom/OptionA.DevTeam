using DevTeam.Core;

namespace DevTeam.UnitTests.Tests;

internal static class ReconServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildReconPrompt_IncludesGoal_WhenProvided", BuildReconPrompt_IncludesGoal_WhenProvided),
        new("BuildReconPrompt_OmitsGoalSection_WhenGoalIsNull", BuildReconPrompt_OmitsGoalSection_WhenGoalIsNull),
        new("BuildReconPrompt_OmitsGoalSection_WhenGoalIsEmpty", BuildReconPrompt_OmitsGoalSection_WhenGoalIsEmpty),
        new("BuildReconPrompt_IncludesRepoRoot", BuildReconPrompt_IncludesRepoRoot),
        new("BuildReconPrompt_ExpectsStructuredResponse", BuildReconPrompt_ExpectsStructuredResponse),
        new("RunAsync_StoresSummaryAsCodebaseContext", RunAsync_StoresSummaryAsCodebaseContext),
        new("RunAsync_FallsBackToRawOutput_WhenSummaryEmpty", RunAsync_FallsBackToRawOutput_WhenSummaryEmpty),
        new("BuildPrompt_InjectsCodebaseContext_ForPlannerRole", BuildPrompt_InjectsCodebaseContext_ForPlannerRole),
        new("BuildPrompt_OmitsCodebaseContext_WhenEmpty", BuildPrompt_OmitsCodebaseContext_WhenEmpty),
        new("BuildPrompt_OmitsCodebaseContext_ForTesterRole", BuildPrompt_OmitsCodebaseContext_ForTesterRole),
        new("WorkspaceStore_WritesCodebaseContextFile_WhenPresent", WorkspaceStore_WritesCodebaseContextFile_WhenPresent),
        new("WorkspaceStore_DoesNotWriteContextFile_WhenEmpty", WorkspaceStore_DoesNotWriteContextFile_WhenEmpty),
    ];

    private static Task BuildReconPrompt_IncludesGoal_WhenProvided()
    {
        var prompt = ReconService.BuildReconPrompt("/repo", "Build a REST API");
        Assert.Contains("Build a REST API", prompt);
        Assert.Contains("reconnaissance", prompt);
        Assert.Contains("READ-ONLY", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_OmitsGoalSection_WhenGoalIsNull()
    {
        var prompt = ReconService.BuildReconPrompt("/repo", null);
        Assert.Contains("reconnaissance", prompt);
        Assert.DoesNotContain("Active goal", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_OmitsGoalSection_WhenGoalIsEmpty()
    {
        var prompt = ReconService.BuildReconPrompt("/repo", "   ");
        Assert.DoesNotContain("Active goal", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_IncludesRepoRoot()
    {
        var prompt = ReconService.BuildReconPrompt("/some/path", null);
        Assert.Contains("/some/path", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildReconPrompt_ExpectsStructuredResponse()
    {
        var prompt = ReconService.BuildReconPrompt("/repo", null);
        Assert.Contains("OUTCOME:", prompt);
        Assert.Contains("SUMMARY:", prompt);
        return Task.CompletedTask;
    }

    private static async Task RunAsync_StoresSummaryAsCodebaseContext()
    {
        var output = "OUTCOME: completed\nSUMMARY:\n## Tech stack\n.NET 10\nISSUES:\n(none)\nSUPERPOWERS_USED:\n(none)\nTOOLS_USED:\n- rg\nQUESTIONS:\n(none)";
        var agent = new RecordingAgentClient(output);
        var factory = new FuncAgentClientFactory(_ => agent);
        var fs = new InMemoryFileSystem();
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            ActiveGoal = new GoalState { GoalText = "Test goal" },
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };
        var store = new WorkspaceStore(".devteam", fs);

        var svc = new ReconService(factory);
        var context = await svc.RunAsync(state, store, "fake", TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Contains(".NET 10", context);
        Assert.That(state.CodebaseContext == context, $"Expected CodebaseContext to equal returned context");
    }

    private static async Task RunAsync_FallsBackToRawOutput_WhenSummaryEmpty()
    {
        var agent = new RecordingAgentClient("No structured output here — just raw text");
        var factory = new FuncAgentClientFactory(_ => agent);
        var fs = new InMemoryFileSystem();
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };
        var store = new WorkspaceStore(".devteam", fs);

        var svc = new ReconService(factory);
        var context = await svc.RunAsync(state, store, "fake", TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Contains("raw text", context);
    }

    private static Task BuildPrompt_InjectsCodebaseContext_ForPlannerRole()
    {
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            CodebaseContext = "## Tech stack\nNode.js 20",
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Plan something", RoleSlug = "planner", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.Contains("Codebase context", prompt);
        Assert.Contains("Node.js 20", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_OmitsCodebaseContext_WhenEmpty()
    {
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            CodebaseContext = "",
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Plan something", RoleSlug = "planner", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.DoesNotContain("Codebase context", prompt);
        return Task.CompletedTask;
    }

    private static Task BuildPrompt_OmitsCodebaseContext_ForTesterRole()
    {
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            CodebaseContext = "## Tech stack\nNode.js 20",
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };
        var issue = new IssueItem { Id = 1, Title = "Test something", RoleSlug = "tester", Status = ItemStatus.Open };
        state.Issues.Add(issue);

        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);

        Assert.DoesNotContain("Codebase context", prompt);
        return Task.CompletedTask;
    }

    private static Task WorkspaceStore_WritesCodebaseContextFile_WhenPresent()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(".devteam", fs);
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            CodebaseContext = "## Tech stack\nGo 1.22",
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };

        store.Save(state);

        var path = Path.Combine(store.WorkspacePath, "codebase-context.md");
        Assert.That(fs.FileExists(path), $"Expected codebase-context.md at {path} to be written");
        var content = fs.ReadAllText(path);
        Assert.Contains("Go 1.22", content);
        return Task.CompletedTask;
    }

    private static Task WorkspaceStore_DoesNotWriteContextFile_WhenEmpty()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(".devteam", fs);
        var state = new WorkspaceState
        {
            RepoRoot = "/repo",
            CodebaseContext = "",
            Models = [new ModelDefinition { Name = "gpt-5-mini", Cost = 0, IsDefault = true }]
        };

        store.Save(state);

        var path = Path.Combine(store.WorkspacePath, "codebase-context.md");
        Assert.That(!fs.FileExists(path), "Expected codebase-context.md NOT to be written when context is empty");
        return Task.CompletedTask;
    }
}

