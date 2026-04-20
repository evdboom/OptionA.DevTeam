using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class PlanCommandHandler : ICliCommandHandler
{
    private readonly WorkspaceStore _store;
    private readonly DevTeamRuntime _runtime;
    private readonly LoopExecutor _loopExecutor;

    public PlanCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, LoopExecutor loopExecutor)
    {
        _store = store;
        _runtime = runtime;
        _loopExecutor = loopExecutor;
    }

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        await CliLoopHandler.ShowPlanAsync(_store, _runtime, _loopExecutor, state, options, interactive: false);
        return 0;
    }
}
