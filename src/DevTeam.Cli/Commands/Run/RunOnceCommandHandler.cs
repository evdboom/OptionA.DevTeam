using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class RunOnceCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        if (PlanWorkflow.RequiresPlanningBeforeRun(state, _store))
        {
            _output.WriteLine("No plan has been written yet. Run `plan` first.");
            return Task.FromResult(1);
        }

        var maxSubagents = CliOptionParser.GetIntOption(options, "max-subagents", 3);
        var result = _runtime.RunOnce(state, maxSubagents);
        _store.Save(state);
        WorkspaceStatusPrinter.PrintLoopResult(result);
        return Task.FromResult(0);
    }
}
