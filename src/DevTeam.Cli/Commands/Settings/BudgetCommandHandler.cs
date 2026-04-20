using DevTeam.Core;
using static DevTeam.Cli.CliWorkspaceHelper;

namespace DevTeam.Cli;

internal sealed class BudgetCommandHandler(WorkspaceStore store) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        UpdateBudget(state, options);
        _store.Save(state);
        WorkspaceStatusPrinter.PrintBudget(state.Budget);
        return Task.FromResult(0);
    }
}
