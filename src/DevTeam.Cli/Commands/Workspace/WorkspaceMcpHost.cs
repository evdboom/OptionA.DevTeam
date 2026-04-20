using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class WorkspaceMcpHost : IWorkspaceMcpHost
{
    public async Task<int> RunAsync(string workspacePath, string backend, TimeSpan timeout)
    {
        var runtime = new DevTeamRuntime();
        var store = new WorkspaceStore(workspacePath);
        var executor = new LoopExecutor(runtime, store);
        Func<int, string?, CancellationToken, Task<string>> spawnAgent =
            (issueId, contextHint, ct) => executor.SpawnIssueAsync(issueId, contextHint, backend, timeout, ct);
        var server = new WorkspaceMcpServer(workspacePath, spawnAgent);
        await server.RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput());
        return 0;
    }
}
