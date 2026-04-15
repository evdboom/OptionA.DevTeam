namespace DevTeam.TestInfrastructure;

/// <summary>
/// In-memory fake implementation of <see cref="IGitRepository"/> for unit tests.
/// </summary>
public sealed class FakeGitRepository : IGitRepository
{
    public bool IsGitRepositoryResult { get; set; } = true;
    public bool TryCreateWorktreeResult { get; set; } = true;
    public WorktreeMergeResult MergeResult { get; set; } = new(false);

    public List<string> CreatedWorktreePaths { get; } = [];
    public List<string> RemovedWorktreePaths { get; } = [];
    public List<string> MergedBranches { get; } = [];

    public bool IsGitRepository(string workingDirectory) => IsGitRepositoryResult;
    public bool EnsureRepository(string workingDirectory) => false;

    public GitStatusSnapshot? TryCaptureStatus(string workingDirectory) =>
        new() { RepositoryRoot = workingDirectory };

    public IReadOnlyList<string> StagePathsChangedSince(string workingDirectory, GitStatusSnapshot? beforeSnapshot) => [];

    public bool TryCreateWorktree(string repoRoot, string worktreePath, string branchName)
    {
        if (!TryCreateWorktreeResult) return false;
        CreatedWorktreePaths.Add(worktreePath);
        return true;
    }

    public WorktreeMergeResult CommitAndMergeWorktree(string repoRoot, string worktreePath, string branchName, string commitMessage)
    {
        MergedBranches.Add(branchName);
        return MergeResult;
    }

    public void RemoveWorktree(string repoRoot, string worktreePath, string branchName)
    {
        RemovedWorktreePaths.Add(worktreePath);
    }
}
