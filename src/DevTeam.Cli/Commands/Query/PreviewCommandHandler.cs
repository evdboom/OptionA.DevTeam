using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class PreviewCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
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
            return Task.FromResult(0);
        }

        if (PlanWorkflow.IsAwaitingApproval(state, _store))
        {
            _output.WriteLine("A plan is ready. Review it with `plan`, provide feedback, or approve it before starting the loop.");
            return Task.FromResult(0);
        }

        var maxSubagents = GetIntOption(options, "max-subagents", state.Runtime.DefaultMaxSubagents);
        RunPreviewPrinter.PrintPreview(state, _runtime, maxSubagents);
        return Task.FromResult(0);
    }
}
