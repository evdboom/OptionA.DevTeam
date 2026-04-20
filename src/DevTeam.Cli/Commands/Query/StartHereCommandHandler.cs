using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class StartHereCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = File.Exists(_store.StatePath) ? _store.Load() : null;
        var persona = GetPositionalValue(options);
        _output.WriteLine(DevTeam.Cli.Shell.NonInteractiveShellHost.StripMarkup(
            OnboardingGuideBuilder.BuildMarkup(state, _runtime, persona)));
        return Task.FromResult(0);
    }
}
