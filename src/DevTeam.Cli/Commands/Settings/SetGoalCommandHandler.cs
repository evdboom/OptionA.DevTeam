using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class SetGoalCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var goal = GoalInputResolver.Resolve(
            GetPositionalValue(options),
            GetOption(options, "goal-file"),
            state.RepoRoot)
            ?? throw new InvalidOperationException("Missing goal text. Provide inline text or --goal-file PATH.");
        _runtime.SetGoal(state, goal);
        _store.Save(state);
        _output.WriteLine("Updated active goal.");
        return Task.FromResult(0);
    }
}
