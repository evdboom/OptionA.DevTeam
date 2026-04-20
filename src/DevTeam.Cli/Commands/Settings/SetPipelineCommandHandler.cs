using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class SetPipelineCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var values = GetPositionalValues(options);
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Usage: set-pipeline <role...|default>");
        }

        if (values.Count == 1 && string.Equals(values[0], "default", StringComparison.OrdinalIgnoreCase))
        {
            _runtime.ResetDefaultPipelineRoles(state);
            _store.Save(state);
            _output.WriteLine($"Reset pipeline to mode default: {string.Join(" -> ", state.Runtime.DefaultPipelineRoles)}");
            return Task.FromResult(0);
        }

        _runtime.SetDefaultPipelineRoles(state, values);
        _store.Save(state);
        _output.WriteLine($"Updated pipeline: {string.Join(" -> ", state.Runtime.DefaultPipelineRoles)}");
        return Task.FromResult(0);
    }
}
