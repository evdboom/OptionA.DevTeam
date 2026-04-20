using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class FeedbackCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var feedback = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing feedback text.");
        _runtime.RecordPlanningFeedback(state, feedback);
        _store.Save(state);
        _output.WriteLine(state.Phase == WorkflowPhase.ArchitectPlanning
            ? "Captured architect plan feedback."
            : "Captured planning feedback.");
        return Task.FromResult(0);
    }
}
