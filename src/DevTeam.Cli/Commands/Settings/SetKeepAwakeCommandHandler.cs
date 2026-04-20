using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class SetKeepAwakeCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;
    private const string EnabledOption = "enabled";

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var requested = GetPositionalValue(options) ?? GetOption(options, EnabledOption)
            ?? throw new InvalidOperationException("Usage: set-keep-awake <true|false> [--workspace PATH]");
        var enabled = ParseBoolOrThrow(requested, "Usage: set-keep-awake <true|false> [--workspace PATH]");
        _runtime.SetKeepAwake(state, enabled);
        _store.Save(state);
        _output.WriteLine($"Updated keep-awake setting to {(enabled ? "enabled" : "disabled")}.");
        return Task.FromResult(0);
    }
}
