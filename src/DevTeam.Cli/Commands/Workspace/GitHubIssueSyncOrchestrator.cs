using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class GitHubIssueSyncOrchestrator(GitHubIssueSyncService syncService) : IGitHubIssueSyncOrchestrator
{
    private readonly GitHubIssueSyncService _syncService = syncService;

    public Task<GitHubSyncReport> SyncAsync(WorkspaceState state, DevTeamRuntime runtime, string repositoryRoot, CancellationToken cancellationToken)
    {
        return _syncService.SyncAsync(state, runtime, repositoryRoot, cancellationToken);
    }
}
