using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class UiHarnessCommandHandler : ICliCommandHandler
{
    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options) =>
        await UiHarness.RunAsync(options);
}
