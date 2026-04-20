using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class WorkspaceMcpCommandHandler(IWorkspaceMcpHost workspaceMcpHost) : ICliCommandHandler
{
    private readonly IWorkspaceMcpHost _workspaceMcpHost = workspaceMcpHost;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var workspace = GetOption(options, "workspace") ?? ".devteam";
        var mcpBackend = GetOption(options, "backend") ?? "sdk";
        var mcpTimeout = TimeSpan.FromSeconds(GetIntOption(options, "timeout-seconds", 600));
        return await _workspaceMcpHost.RunAsync(workspace, mcpBackend, mcpTimeout);
    }
}

