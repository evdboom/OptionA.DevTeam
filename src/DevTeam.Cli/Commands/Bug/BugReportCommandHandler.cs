using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class BugReportCommandHandler : ICliCommandHandler
{
    private readonly WorkspaceStore _store;
    private readonly DevTeamRuntime _runtime;

    public BugReportCommandHandler(WorkspaceStore store, DevTeamRuntime runtime)
    {
        _store = store;
        _runtime = runtime;
    }

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options) =>
        CliWorkspaceHelper.EmitBugReport(_store, _runtime, options, shellDiagnostics: null);
}
