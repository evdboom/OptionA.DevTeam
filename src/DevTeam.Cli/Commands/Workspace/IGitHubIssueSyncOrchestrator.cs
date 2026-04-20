using DevTeam.Core;

namespace DevTeam.Cli;

internal interface IGitHubIssueSyncOrchestrator
{
    Task<GitHubSyncReport> SyncAsync(WorkspaceState state, DevTeamRuntime runtime, string repositoryRoot, CancellationToken cancellationToken);
}
