using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class CompleteRunCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var runId = GetNullableIntOption(options, "run-id") ?? throw new InvalidOperationException("Missing --run-id.");
        var outcome = GetOption(options, "outcome") ?? throw new InvalidOperationException("Missing --outcome.");
        var summary = GetOption(options, "summary") ?? throw new InvalidOperationException("Missing --summary.");
        _runtime.CompleteRun(
            state,
            new CompleteRunRequest
            {
                RunId = runId,
                Outcome = outcome,
                Summary = summary
            });
        _store.Save(state);
        _output.WriteLine($"Updated run #{runId} as {outcome}");
        return Task.FromResult(0);
    }
}
