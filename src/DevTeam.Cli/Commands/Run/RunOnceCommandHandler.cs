using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class RunOnceCommandHandler : ICliCommandHandler
{
    private readonly WorkspaceStore _store;
    private readonly DevTeamRuntime _runtime;
    private readonly IConsoleOutput _output;

    public RunOnceCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output)
    {
        _store = store;
        _runtime = runtime;
        _output = output;
    }

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
