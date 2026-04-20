using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class QuestionsCommandHandler(WorkspaceStore store, DevTeamRuntime runtime) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        WorkspaceStatusPrinter.PrintQuestions(state, _store, _runtime);
        return Task.FromResult(0);
    }
}
