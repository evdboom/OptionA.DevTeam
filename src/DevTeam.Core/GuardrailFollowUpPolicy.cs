namespace DevTeam.Core;

internal sealed class GuardrailFollowUpPolicy
{
    private const string RoleDeveloper = "developer";
    private const string RoleBackendDeveloper = "backend-developer";
    private const string RoleFrontendDeveloper = "frontend-developer";
    private const string RoleFullstackDeveloper = "fullstack-developer";
    private const string RoleReviewer = "reviewer";
    private const string RoleAuditor = "auditor";
    private const int ReviewerChangedPathsThreshold = 3;
    private const int ReviewerComplexityThreshold = 60;
    private const int ReviewerCreatedIssuesThreshold = 2;
    private const int AuditorChangedPathsThreshold = 8;
    private const int AuditorRunCadenceThreshold = 5;

    private readonly IIssueService _issueService;

    public GuardrailFollowUpPolicy(IIssueService issueService)
    {
        _issueService = issueService;
    }

    public void EnsureFollowUps(WorkspaceState state, IssueItem completedIssue, AgentRun completedRun)
    {
        if (!IsImplementationRole(completedIssue.RoleSlug))
        {
            return;
        }

        var changedCount = completedRun.ChangedPaths.Count;
        var createdIssueCount = completedRun.CreatedIssueIds.Count;
        var hasMeaningfulChanges = changedCount >= ReviewerChangedPathsThreshold
            || (completedIssue.ComplexityHint ?? 0) >= ReviewerComplexityThreshold
            || createdIssueCount >= ReviewerCreatedIssuesThreshold;

        if (hasMeaningfulChanges)
        {
            TryCreateReviewerFollowUp(state, completedIssue, changedCount, createdIssueCount);
        }

        TryCreateAuditorFollowUp(state, completedIssue, changedCount);
    }

    private void TryCreateReviewerFollowUp(
        WorkspaceState state,
        IssueItem completedIssue,
        int changedCount,
        int createdIssueCount)
    {
        if (!RoleExists(state, RoleReviewer))
        {
            return;
        }

        if (PipelineContainsRole(state, completedIssue.PipelineId, RoleReviewer))
        {
            return;
        }

        var alreadyQueued = state.Issues.Any(item =>
            item.Status != ItemStatus.Done
            && string.Equals(item.RoleSlug, RoleReviewer, StringComparison.OrdinalIgnoreCase)
            && item.DependsOnIssueIds.Contains(completedIssue.Id));
        if (alreadyQueued)
        {
            return;
        }

        var request = new IssueRequest
        {
            Title = $"Review {completedIssue.Title}",
            Detail =
                $"Guardrail review after implementation issue #{completedIssue.Id}. " +
                $"Changed paths: {changedCount}; follow-on issues created: {createdIssueCount}. " +
                "Focus on correctness, regressions, and maintainability.",
            RoleSlug = RoleReviewer,
            Priority = Math.Clamp(Math.Max(60, completedIssue.Priority - 3), 1, 100),
            RoadmapItemId = completedIssue.RoadmapItemId,
            DependsOn = [completedIssue.Id],
            Area = completedIssue.Area,
            FamilyKey = completedIssue.FamilyKey,
            ParentIssueId = completedIssue.Id,
            PipelineId = null,
            PipelineStageIndex = null,
            ComplexityHint = null
        };

        _issueService.CreateIssue(state, request);
    }

    private void TryCreateAuditorFollowUp(WorkspaceState state, IssueItem completedIssue, int changedCount)
    {
        if (!RoleExists(state, RoleAuditor))
        {
            return;
        }

        var hasOpenAuditor = state.Issues.Any(item =>
            (item.Status is ItemStatus.Open or ItemStatus.InProgress)
            && string.Equals(item.RoleSlug, RoleAuditor, StringComparison.OrdinalIgnoreCase));
        if (hasOpenAuditor)
        {
            return;
        }

        var lastAuditCompletedAt = state.AgentRuns
            .Where(run => run.Status == AgentRunStatus.Completed && string.Equals(run.RoleSlug, RoleAuditor, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.UpdatedAtUtc)
            .Select(run => run.UpdatedAtUtc)
            .FirstOrDefault(DateTimeOffset.MinValue);

        var completedImplementationRunsSinceLastAudit = state.AgentRuns
            .Where(run =>
                run.Status == AgentRunStatus.Completed
                && IsImplementationRole(run.RoleSlug)
                && run.UpdatedAtUtc >= lastAuditCompletedAt)
            .ToList();

        var shouldQueueAudit = changedCount >= AuditorChangedPathsThreshold
            || completedImplementationRunsSinceLastAudit.Count >= AuditorRunCadenceThreshold;
        if (!shouldQueueAudit)
        {
            return;
        }

        var dependencyIds = completedImplementationRunsSinceLastAudit
            .Select(run => run.IssueId)
            .Distinct()
            .Take(8)
            .ToList();
        if (dependencyIds.Count == 0)
        {
            dependencyIds.Add(completedIssue.Id);
        }

        var trigger = changedCount >= AuditorChangedPathsThreshold
            ? "large change footprint"
            : "scheduled guardrail cadence";

        var request = new IssueRequest
        {
            Title = "Audit recent execution drift",
            Detail =
                $"Guardrail audit triggered by {trigger}. " +
                $"Recent implementation runs since last audit: {completedImplementationRunsSinceLastAudit.Count}. " +
                "Assess auditable/testable/maintainable drift and create focused remediation issues.",
            RoleSlug = RoleAuditor,
            Priority = Math.Clamp(Math.Max(58, completedIssue.Priority - 8), 1, 100),
            RoadmapItemId = completedIssue.RoadmapItemId,
            DependsOn = dependencyIds,
            Area = "repo-audit",
            FamilyKey = "repo-audit",
            ParentIssueId = null,
            PipelineId = null,
            PipelineStageIndex = null,
            ComplexityHint = 75
        };

        _issueService.CreateIssue(state, request);
    }

    private static bool PipelineContainsRole(WorkspaceState state, int? pipelineId, string roleSlug)
    {
        if (pipelineId is null)
        {
            return false;
        }

        var pipeline = state.Pipelines.FirstOrDefault(item => item.Id == pipelineId.Value);
        return pipeline is not null
            && pipeline.RoleSequence.Any(item => string.Equals(item, roleSlug, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RoleExists(WorkspaceState state, string roleSlug) =>
        state.Roles.Any(role => string.Equals(role.Slug, roleSlug, StringComparison.OrdinalIgnoreCase));

    private static bool IsImplementationRole(string roleSlug)
    {
        var normalized = roleSlug.Trim().ToLowerInvariant();
        return normalized is RoleDeveloper or RoleBackendDeveloper or RoleFrontendDeveloper or RoleFullstackDeveloper;
    }
}
