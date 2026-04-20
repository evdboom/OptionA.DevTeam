using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class StatusCommandHandler(WorkspaceStore store, DevTeamRuntime runtime) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        WorkspaceStatusPrinter.PrintStatus(_store.Load(), _runtime);
        return Task.FromResult(0);
    }
}
