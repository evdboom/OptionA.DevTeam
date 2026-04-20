using static DevTeam.Cli.CliOptionParser;
using static DevTeam.Cli.CliWorkspaceHelper;

namespace DevTeam.Cli;

internal sealed class CustomizeWorkspaceCommandHandler(IConsoleOutput output) : ICliCommandHandler
{
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var target = Path.Combine(Environment.CurrentDirectory, ".devteam-source");
        var force = GetBoolOption(options, "force", false);
        CopyPackagedAssets(target, force);
        ExportGitHubSkills(Environment.CurrentDirectory, force, _output.WriteLine);
        return Task.FromResult(0);
    }
}
