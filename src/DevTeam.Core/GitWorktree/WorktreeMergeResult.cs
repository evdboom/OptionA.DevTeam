namespace DevTeam.Core;

/// <summary>Result of committing changes in a worktree and merging the branch into the base branch.</summary>
public sealed record WorktreeMergeResult(bool HasConflicts, string ConflictSummary = "");
