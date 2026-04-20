using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class StartShellCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, LoopExecutor loopExecutor, ToolUpdateService toolUpdateService) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly LoopExecutor _loopExecutor = loopExecutor;
    private readonly ToolUpdateService _toolUpdateService = toolUpdateService;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options) =>
        await CliLoopHandler.RunShellAsync(_store, _runtime, _loopExecutor, _toolUpdateService, options);
}
