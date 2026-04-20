using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class WorkspaceReconRunner(ReconService reconService) : IWorkspaceReconRunner
{
    private readonly ReconService _reconService = reconService;

    public Task<string> RunAsync(WorkspaceState state, WorkspaceStore store, string backend, TimeSpan timeout, CancellationToken cancellationToken)
    {
        return _reconService.RunAsync(state, store, backend, timeout, cancellationToken);
    }
}
