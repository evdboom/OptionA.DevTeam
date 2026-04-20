using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class PlanCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, LoopExecutor loopExecutor) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly LoopExecutor _loopExecutor = loopExecutor;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        await CliLoopHandler.ShowPlanAsync(_store, _runtime, _loopExecutor, state, options, interactive: false);
        return 0;
    }
}
