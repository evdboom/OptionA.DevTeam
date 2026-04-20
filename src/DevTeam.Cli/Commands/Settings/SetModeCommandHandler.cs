using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class SetModeCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var mode = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing mode slug.");
        _runtime.SetMode(state, mode);
        _store.Save(state);
        _output.WriteLine($"Updated active mode to {state.Runtime.ActiveModeSlug}.");
        return Task.FromResult(0);
    }
}
