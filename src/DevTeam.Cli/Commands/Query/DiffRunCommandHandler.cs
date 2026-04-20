using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class DiffRunCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        _ = _runtime;
        var state = _store.Load();
        var values = GetPositionalValues(options);
        if (values.Count is < 1 or > 2 || !int.TryParse(values[0], out var runId) || (values.Count == 2 && !int.TryParse(values[1], out _)))
        {
            throw new InvalidOperationException("Usage: diff-run <run-id> [compare-run-id]");
        }

        var compareRunId = values.Count == 2 ? int.Parse(values[1]) : (int?)null;
        var report = DevTeamRuntime.BuildRunDiff(state, runId, compareRunId);
        _output.WriteLine(DevTeam.Cli.Shell.NonInteractiveShellHost.StripMarkup(RunDiffPrinter.BuildMarkup(report)));
        return Task.FromResult(0);
    }
}
