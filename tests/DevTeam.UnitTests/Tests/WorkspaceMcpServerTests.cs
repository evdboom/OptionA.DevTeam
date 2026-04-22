using System.Text;
using System.Text.Json;

namespace DevTeam.UnitTests.Tests;

internal static class WorkspaceMcpServerTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("ToolsList_ExcludesSpawnAgent_WhenNoRunner", ToolsList_ExcludesSpawnAgent_WhenNoRunner),
        new("ToolsList_IncludesSpawnAgent_WhenRunnerProvided", ToolsList_IncludesSpawnAgent_WhenRunnerProvided),
        new("SpawnAgentTool_InvokesRunner_WithCorrectIssueId", SpawnAgentTool_InvokesRunner_WithCorrectIssueId),
        new("SpawnAgentTool_ForwardsContextHint", SpawnAgentTool_ForwardsContextHint),
        new("Initialize_RespondsWithServerInfo", Initialize_RespondsWithServerInfo),
        new("ToolsList_IncludesGetIssueAndGetDecisions", ToolsList_IncludesGetIssueAndGetDecisions),
        new("GetIssueTool_ReturnsIssueWithRefinementFields", GetIssueTool_ReturnsIssueWithRefinementFields),
        new("GetIssueTool_ReturnsError_WhenIssueNotFound", GetIssueTool_ReturnsError_WhenIssueNotFound),
        new("GetDecisionsTool_ReturnsOnlyRequestedDecisions", GetDecisionsTool_ReturnsOnlyRequestedDecisions),
        new("GetDecisionsTool_ReturnsEmpty_WhenNoIdsGiven", GetDecisionsTool_ReturnsEmpty_WhenNoIdsGiven),
        new("CreateIssueTool_ReturnsError_WhenTitleTooLong", CreateIssueTool_ReturnsError_WhenTitleTooLong),
    ];

    private const string TestWorkspace = "test-ws";
    private const string TestRepoRoot = @"C:\test-repo";
    private const string McpInitialize = "initialize";
    private const string McpResult = "result";
    private const string McpToolsCall = "tools/call";
    private const string Architect = CoreConstants.Roles.Architect;

    private static async Task<JsonDocument> SendMcpRequest(WorkspaceMcpServer server, string method, object? @params = null, string id = "1")
    {
        var request = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null)
        {
            request["params"] = @params;
        }

        var requestJson = JsonSerializer.Serialize(request);
        var requestBytes = Encoding.UTF8.GetBytes(requestJson);
        var inputPayload = $"Content-Length: {requestBytes.Length}\r\n\r\n{requestJson}";

        using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(inputPayload));
        using var outputStream = new MemoryStream();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Run server and stop it after it writes one response (when input is exhausted)
        try
        {
            await server.RunAsync(inputStream, outputStream, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected — server stops when cancelled
        }

        outputStream.Position = 0;
        var responseText = Encoding.UTF8.GetString(outputStream.ToArray());

        // Strip Content-Length header
        var headerEnd = responseText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var json = headerEnd >= 0 ? responseText[(headerEnd + 4)..] : responseText;

        return JsonDocument.Parse(json);
    }

    private static async Task ToolsList_ExcludesSpawnAgent_WhenNoRunner()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        // No subAgentRunner — spawn_agent should not appear
        var server = new WorkspaceMcpServer(TestWorkspace);

        // First initialize, then list tools
        await SendMcpRequest(server, McpInitialize);
        using var response = await SendMcpRequest(server, "tools/list");

        var tools = response.RootElement.GetProperty(McpResult).GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.That(!names.Contains("spawn_agent"), $"Expected no spawn_agent but tools were: {string.Join(", ", names)}");
        Assert.That(names.Contains("get_workspace_summary"), $"Expected get_workspace_summary in tools");
    }

    private static async Task ToolsList_IncludesSpawnAgent_WhenRunnerProvided()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        Func<int, string?, CancellationToken, Task<string>> runner = (_, _, _) => Task.FromResult("Issue completed");
        var server = new WorkspaceMcpServer(TestWorkspace, runner);

        await SendMcpRequest(server, McpInitialize);
        using var response = await SendMcpRequest(server, "tools/list");

        var tools = response.RootElement.GetProperty(McpResult).GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.That(names.Contains("spawn_agent"), $"Expected spawn_agent but tools were: {string.Join(", ", names)}");
    }

    private static async Task SpawnAgentTool_InvokesRunner_WithCorrectIssueId()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        var capturedIssueId = -1;
        Func<int, string?, CancellationToken, Task<string>> runner = (issueId, _, _) =>
        {
            capturedIssueId = issueId;
            return Task.FromResult($"Issue #{issueId} completed");
        };
        var server = new WorkspaceMcpServer(TestWorkspace, runner);

        await SendMcpRequest(server, McpInitialize);
        await SendMcpRequest(server, McpToolsCall, new
        {
            name = "spawn_agent",
            arguments = new { issueId = 42 }
        });

        Assert.That(capturedIssueId == 42, $"Expected runner called with issueId=42 but got {capturedIssueId}");
    }

    private static async Task SpawnAgentTool_ForwardsContextHint()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        var capturedIssueId = -1;
        string? capturedContextHint = null;
        Func<int, string?, CancellationToken, Task<string>> runner = (issueId, contextHint, _) =>
        {
            capturedIssueId = issueId;
            capturedContextHint = contextHint;
            return Task.FromResult($"Issue #{issueId} completed");
        };
        var server = new WorkspaceMcpServer(TestWorkspace, runner);

        await SendMcpRequest(server, McpInitialize);
        await SendMcpRequest(server, McpToolsCall, new
        {
            name = "spawn_agent",
            arguments = new { issueId = 42, contextHint = "Use the release-branch API notes already gathered by orchestrator." }
        });

        Assert.That(capturedIssueId == 42, $"Expected runner called with issueId=42 but got {capturedIssueId}");
        Assert.That(string.Equals(capturedContextHint, "Use the release-branch API notes already gathered by orchestrator.", StringComparison.Ordinal),
            $"Expected contextHint to be forwarded but got: {capturedContextHint}");
    }

    private static async Task Initialize_RespondsWithServerInfo()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        var server = new WorkspaceMcpServer(TestWorkspace);
        using var response = await SendMcpRequest(server, McpInitialize);

        var serverInfo = response.RootElement.GetProperty(McpResult).GetProperty("serverInfo");
        var name = serverInfo.GetProperty("name").GetString();

        Assert.That(name == "devteam-workspace", $"Expected serverInfo.name='devteam-workspace' but got '{name}'");
    }

    private static async Task ToolsList_IncludesGetIssueAndGetDecisions()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        var server = new WorkspaceMcpServer(TestWorkspace);

        await SendMcpRequest(server, McpInitialize);
        using var response = await SendMcpRequest(server, "tools/list");

        var tools = response.RootElement.GetProperty(McpResult).GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.That(names.Contains("get_issue"), $"Expected get_issue in tools but got: {string.Join(", ", names)}");
        Assert.That(names.Contains("get_decisions"), $"Expected get_decisions in tools but got: {string.Join(", ", names)}");
    }

    private static async Task GetIssueTool_ReturnsIssueWithRefinementFields()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid():N}");
        try
        {
            var store = new WorkspaceStore(workspacePath);
            var state = store.Initialize(TestRepoRoot, 100, 20);

            var runtime = new DevTeamRuntime();
            var issue = runtime.AddIssue(state, new IssueRequest
            {
                Title = "Implement feature",
                Detail = "Add the component",
                RoleSlug = "developer",
                Priority = 60
            });
            issue.FilesInScope.Add("src/Components/MyComponent.razor");
            issue.LinkedDecisionIds.Add(5);
            issue.RefinementState = IssueRefinementState.ReadyToPickup;
            store.Save(state);

            var server = new WorkspaceMcpServer(workspacePath);

            await SendMcpRequest(server, McpInitialize);
            using var response = await SendMcpRequest(server, McpToolsCall, new
            {
                name = "get_issue",
                arguments = new { issueId = issue.Id }
            });

            var result = response.RootElement.GetProperty(McpResult);
            var content = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            var issueJson = JsonDocument.Parse(content).RootElement;

            Assert.That(issueJson.GetProperty("id").GetInt32() == issue.Id,
                $"Expected id {issue.Id}");
            Assert.That(issueJson.GetProperty("title").GetString() == "Implement feature",
                "Expected title 'Implement feature'");
            Assert.That(issueJson.GetProperty("refinementState").GetString() == "ReadyToPickup",
                $"Expected refinementState=ReadyToPickup but got {issueJson.GetProperty("refinementState").GetString()}");

            var filesInScope = issueJson.GetProperty("filesInScope");
            Assert.That(filesInScope.GetArrayLength() == 1, $"Expected 1 file in scope but got {filesInScope.GetArrayLength()}");
            Assert.That(filesInScope[0].GetString() == "src/Components/MyComponent.razor",
                "Expected the component file in scope");

            var linkedDecisions = issueJson.GetProperty("linkedDecisionIds");
            Assert.That(linkedDecisions.GetArrayLength() == 1, $"Expected 1 linked decision but got {linkedDecisions.GetArrayLength()}");
            Assert.That(linkedDecisions[0].GetInt32() == 5, "Expected decision id 5");
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }

    private static async Task GetIssueTool_ReturnsError_WhenIssueNotFound()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore(TestWorkspace, fs);
        store.Initialize(TestRepoRoot, 100, 20);

        var server = new WorkspaceMcpServer(TestWorkspace);

        await SendMcpRequest(server, McpInitialize);
        using var response = await SendMcpRequest(server, McpToolsCall, new
        {
            name = "get_issue",
            arguments = new { issueId = 9999 }
        });

        // The error should surface in the response (either error field or error message in content)
        var responseText = response.RootElement.ToString();
        Assert.That(responseText.Contains("9999") || responseText.Contains("error", StringComparison.OrdinalIgnoreCase),
            $"Expected error response for missing issue but got: {responseText}");
    }

    private static async Task GetDecisionsTool_ReturnsOnlyRequestedDecisions()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid():N}");
        try
        {
            var store = new WorkspaceStore(workspacePath);
            var state = store.Initialize(TestRepoRoot, 100, 20);

            var runtime = new DevTeamRuntime();
            var d1 = runtime.RecordDecision(state, "Use Playground", "Playground is the vehicle", Architect, null, null, null);
            var d2 = runtime.RecordDecision(state, "Bootstrap Default", "Use Bootstrap for CSS", Architect, null, null, null);
            runtime.RecordDecision(state, "Unrelated Decision", "Something else entirely", Architect, null, null, null);
            store.Save(state);

            var server = new WorkspaceMcpServer(workspacePath);

            await SendMcpRequest(server, McpInitialize);
            using var response = await SendMcpRequest(server, McpToolsCall, new
            {
                name = "get_decisions",
                arguments = new { decisionIds = new[] { d1.Id, d2.Id } }
            });

            var result = response.RootElement.GetProperty(McpResult);
            var content = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            var decisionsJson = JsonDocument.Parse(content).RootElement;

            var decisions = decisionsJson.GetProperty("decisions");
            Assert.That(decisions.GetArrayLength() == 2, $"Expected 2 decisions but got {decisions.GetArrayLength()}");

            var titles = decisions.EnumerateArray().Select(d => d.GetProperty("title").GetString()).ToList();
            Assert.That(titles.Contains("Use Playground"), "Expected 'Use Playground' decision");
            Assert.That(titles.Contains("Bootstrap Default"), "Expected 'Bootstrap Default' decision");
            Assert.That(!titles.Contains("Unrelated Decision"), "Expected 'Unrelated Decision' to be excluded");
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }

    private static async Task GetDecisionsTool_ReturnsEmpty_WhenNoIdsGiven()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid():N}");
        try
        {
            var store = new WorkspaceStore(workspacePath);
            var state = store.Initialize(TestRepoRoot, 100, 20);

            var runtime = new DevTeamRuntime();
            runtime.RecordDecision(state, "Some Decision", "detail", Architect, null, null, null);
            store.Save(state);

            var server = new WorkspaceMcpServer(workspacePath);

            await SendMcpRequest(server, McpInitialize);
            using var response = await SendMcpRequest(server, McpToolsCall, new
            {
                name = "get_decisions",
                arguments = new { decisionIds = Array.Empty<int>() }
            });

            var result = response.RootElement.GetProperty(McpResult);
            var content = result.GetProperty("content")[0].GetProperty("text").GetString()!;
            var decisionsJson = JsonDocument.Parse(content).RootElement;

            var decisions = decisionsJson.GetProperty("decisions");
            Assert.That(decisions.GetArrayLength() == 0, $"Expected 0 decisions but got {decisions.GetArrayLength()}");
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }

    private static async Task CreateIssueTool_ReturnsError_WhenTitleTooLong()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid():N}");
        try
        {
            var store = new WorkspaceStore(workspacePath);
            store.Initialize(TestRepoRoot, 100, 20);

            var server = new WorkspaceMcpServer(workspacePath);

            await SendMcpRequest(server, McpInitialize);
            using var response = await SendMcpRequest(server, McpToolsCall, new
            {
                name = "create_issue",
                arguments = new
                {
                    title = new string('x', 201),
                    detail = "too long title test"
                }
            });

            var error = response.RootElement.GetProperty("error");
            var code = error.GetProperty("code").GetInt32();
            var message = error.GetProperty("message").GetString() ?? string.Empty;

            Assert.That(code == -32000, $"Expected JSON-RPC application error code -32000 but got {code}");
            Assert.That(message.Contains("Argument 'title' exceeds maximum length of 200 characters.", StringComparison.Ordinal),
                $"Expected specific title max-length validation message but got: {message}");
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
    }
}
