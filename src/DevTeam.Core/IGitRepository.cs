namespace DevTeam.Core;

public interface IGitRepository
{
    bool IsGitRepository(string workingDirectory);
    bool EnsureRepository(string workingDirectory);
    GitStatusSnapshot? TryCaptureStatus(string workingDirectory);
    IReadOnlyList<string> StagePathsChangedSince(string workingDirectory, GitStatusSnapshot? beforeSnapshot);

    /// <summary>Creates a git worktree at <paramref name="worktreePath"/> on a new branch <paramref name="branchName"/>.
    /// Returns true on success; returns false (without throwing) when the worktree cannot be created.</summary>
    bool TryCreateWorktree(string repoRoot, string worktreePath, string branchName);

    /// <summary>
    /// Stages all modified files in <paramref name="worktreePath"/>, commits them to the worktree branch, then
    /// merges the branch into the current branch of <paramref name="repoRoot"/> using <c>--no-ff</c>.
    /// Returns a result indicating whether conflicts were encountered.
    /// </summary>
    WorktreeMergeResult CommitAndMergeWorktree(string repoRoot, string worktreePath, string branchName, string commitMessage);

    /// <summary>Removes the worktree at <paramref name="worktreePath"/> and deletes the local <paramref name="branchName"/> branch.
    /// Best-effort — does not throw if the worktree or branch no longer exists.</summary>
    void RemoveWorktree(string repoRoot, string worktreePath, string branchName);
}
