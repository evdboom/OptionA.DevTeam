using DevTeam.Core;

namespace DevTeam.Cli;

internal interface IWorkspaceReconRunner
{
    Task<string> RunAsync(WorkspaceState state, WorkspaceStore store, string backend, TimeSpan timeout, CancellationToken cancellationToken);
}
