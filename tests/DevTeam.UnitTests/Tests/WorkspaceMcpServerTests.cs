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
        new("Initialize_RespondsWithServerInfo", Initialize_RespondsWithServerInfo),
    ];

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
        var store = new WorkspaceStore("test-ws", fs);
        store.Initialize("C:\\test-repo", 100, 20);

        // No subAgentRunner — spawn_agent should not appear
        var server = new WorkspaceMcpServer("test-ws");

        // First initialize, then list tools
        await SendMcpRequest(server, "initialize");
        using var response = await SendMcpRequest(server, "tools/list");

        var tools = response.RootElement.GetProperty("result").GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.That(!names.Contains("spawn_agent"), $"Expected no spawn_agent but tools were: {string.Join(", ", names)}");
        Assert.That(names.Contains("get_workspace_summary"), $"Expected get_workspace_summary in tools");
    }

    private static async Task ToolsList_IncludesSpawnAgent_WhenRunnerProvided()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        store.Initialize("C:\\test-repo", 100, 20);

        Func<int, string?, CancellationToken, Task<string>> runner = (_, _, _) => Task.FromResult("Issue completed");
        var server = new WorkspaceMcpServer("test-ws", runner);

        await SendMcpRequest(server, "initialize");
        using var response = await SendMcpRequest(server, "tools/list");

        var tools = response.RootElement.GetProperty("result").GetProperty("tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        Assert.That(names.Contains("spawn_agent"), $"Expected spawn_agent but tools were: {string.Join(", ", names)}");
    }

    private static async Task SpawnAgentTool_InvokesRunner_WithCorrectIssueId()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        store.Initialize("C:\\test-repo", 100, 20);

        var capturedIssueId = -1;
        Func<int, string?, CancellationToken, Task<string>> runner = (issueId, _, _) =>
        {
            capturedIssueId = issueId;
            return Task.FromResult($"Issue #{issueId} completed");
        };
        var server = new WorkspaceMcpServer("test-ws", runner);

        await SendMcpRequest(server, "initialize");
        await SendMcpRequest(server, "tools/call", new
        {
            name = "spawn_agent",
            arguments = new { issueId = 42 }
        });

        Assert.That(capturedIssueId == 42, $"Expected runner called with issueId=42 but got {capturedIssueId}");
    }

    private static async Task Initialize_RespondsWithServerInfo()
    {
        var fs = new InMemoryFileSystem();
        var store = new WorkspaceStore("test-ws", fs);
        store.Initialize("C:\\test-repo", 100, 20);

        var server = new WorkspaceMcpServer("test-ws");
        using var response = await SendMcpRequest(server, "initialize");

        var serverInfo = response.RootElement.GetProperty("result").GetProperty("serverInfo");
        var name = serverInfo.GetProperty("name").GetString();

        Assert.That(name == "devteam-workspace", $"Expected serverInfo.name='devteam-workspace' but got '{name}'");
    }
}
