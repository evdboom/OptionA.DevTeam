namespace DevTeam.Cli;

internal interface IWorkspaceMcpHost
{
    Task<int> RunAsync(string workspacePath, string backend, TimeSpan timeout);
}
