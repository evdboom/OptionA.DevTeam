namespace DevTeam.Core;

public interface IIssueService
{
    IssueItem AddIssue(WorkspaceState state, IssueRequest request);
    IssueItem EditIssue(WorkspaceState state, IssueEditRequest request);
    IssueItem? FindIssue(WorkspaceState state, int issueId);
    IReadOnlyList<IssueItem> GetReadyIssues(WorkspaceState state, int maxCount);
    IReadOnlyList<IssueItem> GetReadyIssueCandidates(WorkspaceState state);
    void EnsurePipelineAssignments(WorkspaceState state);
    void AdvancePipelineAfterCompletion(WorkspaceState state, IssueItem issue);
    void UpdatePipelineStatus(WorkspaceState state, IssueItem issue, PipelineStatus status, int? activeIssueId);
    bool HasBlockingQuestions(WorkspaceState state);
    bool TryResolveRoleSlug(WorkspaceState state, string roleSlug, out string resolvedRoleSlug);
    string ResolveRoleSlug(WorkspaceState state, string roleSlug);
    IReadOnlyDictionary<string, string> GetKnownRoleAliases(WorkspaceState state);
}
