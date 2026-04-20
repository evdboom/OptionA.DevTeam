using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class HelpCommandHandler : ICliCommandHandler
{
    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var all = GetBoolOption(options, "all", false);
        return Task.FromResult(PrintHelpTask(all, exitCode: 0).Result);
    }

    private static Task<int> PrintHelpTask(bool all, int exitCode)
    {
        WorkspaceStatusPrinter.PrintHelp(all);
        return Task.FromResult(exitCode);
    }
}
