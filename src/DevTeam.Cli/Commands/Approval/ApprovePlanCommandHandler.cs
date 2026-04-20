using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;
using static DevTeam.Cli.CliWorkspaceHelper;

namespace DevTeam.Cli;

internal sealed class ApprovePlanCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var note = GetOption(options, "note") ?? GetPositionalValue(options) ?? "User approved the current plan.";
        if (state.Phase == WorkflowPhase.ArchitectPlanning)
        {
            _runtime.ApproveArchitectPlan(state, note);
            _store.Save(state);
            _output.WriteLine("Approved the architect plan. Execution work can now begin.");
            return Task.FromResult(0);
        }

        _runtime.ApprovePlan(state, note);
        _store.Save(state);
        if (state.Phase == WorkflowPhase.ArchitectPlanning)
        {
            _output.WriteLine("Approved the high-level plan. Architect planning phase is next — run the loop to let the architect design the execution plan, then approve again.");
        }
        else
        {
            _output.WriteLine("Approved the current plan. Execution work can now continue.");
        }

        return Task.FromResult(0);
    }
}
