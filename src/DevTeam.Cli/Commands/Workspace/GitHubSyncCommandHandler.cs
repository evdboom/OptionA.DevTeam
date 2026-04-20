using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class GitHubSyncCommandHandler(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    IConsoleOutput output,
    IGitHubIssueSyncOrchestrator syncOrchestrator) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;
    private readonly IGitHubIssueSyncOrchestrator _syncOrchestrator = syncOrchestrator;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var report = await _syncOrchestrator.SyncAsync(state, _runtime, Environment.CurrentDirectory, CancellationToken.None);
        _store.Save(state);
        _output.WriteLine($"GitHub sync complete: {report.ImportedIssueCount} issue(s) imported, {report.UpdatedIssueCount} updated, {report.ImportedQuestionCount} question(s) imported, {report.UpdatedQuestionCount} updated, {report.SkippedCount} skipped.");
        return 0;
    }
}
