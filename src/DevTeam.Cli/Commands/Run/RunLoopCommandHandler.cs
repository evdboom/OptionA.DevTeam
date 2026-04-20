using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class RunLoopCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, LoopExecutor loopExecutor, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly LoopExecutor _loopExecutor = loopExecutor;
    private readonly IConsoleOutput _output = output;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        if (PlanWorkflow.RequiresPlanningBeforeRun(state, _store))
        {
            _output.WriteLine("No plan has been written yet. Run `plan` first.");
            return 1;
        }

        if (PlanWorkflow.IsAwaitingApproval(state, _store))
        {
            _output.WriteLine("A plan is ready. Review it with `plan`, provide feedback, or approve it before starting the loop.");
            return 1;
        }

        if (CliOptionParser.GetBoolOption(options, "dry-run", false))
        {
            var maxSubagents = CliOptionParser.GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
            RunPreviewPrinter.PrintPreview(state, _runtime, maxSubagents);
            _output.WriteLine("Dry run only — nothing was executed.");
            return 0;
        }

        var report = await CliLoopHandler.RunLoopAsync(_store, _runtime, _loopExecutor, state, options);
        _output.WriteLine($"Loop complete after {report.IterationsExecuted} iteration(s). Final state: {report.FinalState}");
        WorkspaceStatusPrinter.PrintBudget(state.Budget);
        if (report.FinalState == "awaiting-architect-approval")
        {
            WorkspaceStatusPrinter.PrintArchitectSummary(state);
        }

        return 0;
    }
}
