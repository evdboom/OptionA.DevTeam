using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class BugReportCommandHandler(WorkspaceStore store, DevTeamRuntime runtime) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options) =>
        CliWorkspaceHelper.EmitBugReport(_store, _runtime, options, shellDiagnostics: null);
}
