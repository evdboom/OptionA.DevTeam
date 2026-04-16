namespace DevTeam.Core;

public sealed partial class IssueService
{
    public IssueItem EditIssue(WorkspaceState state, IssueEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasChanges)
        {
            throw new InvalidOperationException("No issue changes were provided.");
        }

        var issue = FindIssue(state, request.IssueId)
            ?? throw new InvalidOperationException($"Issue #{request.IssueId} not found.");

        if (issue.IsPlanningIssue)
        {
            throw new InvalidOperationException("Planning issues cannot be edited directly. Use /goal, /plan, /feedback, or /approve instead.");
        }

        if (state.AgentRuns.Any(run => run.IssueId == issue.Id && run.Status is AgentRunStatus.Queued or AgentRunStatus.Running))
        {
            throw new InvalidOperationException($"Issue #{issue.Id} is currently queued or running and cannot be edited yet.");
        }

        var changesPipelineTopology = request.RoleSlug is not null || request.DependsOnIssueIds is not null || request.ClearDependencies;
        if (changesPipelineTopology && issue.PipelineId is not null)
        {
            throw new InvalidOperationException("Pipeline-backed issues cannot change role or dependencies. Edit the architect plan or create a replacement issue instead.");
        }

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("Issue title cannot be empty.");
            }

            issue.Title = title;
        }

        if (request.Detail is not null)
        {
            issue.Detail = request.Detail.Trim();
        }

        if (request.RoleSlug is not null)
        {
            issue.RoleSlug = ResolveRoleSlugInternal(state, request.RoleSlug);
        }

        if (request.ClearArea)
        {
            issue.Area = "";
        }
        else if (request.Area is not null)
        {
            issue.Area = NormalizeArea(request.Area);
        }

        if (request.Priority is int priority)
        {
            if (priority is < 1 or > 100)
            {
                throw new InvalidOperationException("Issue priority must be between 1 and 100.");
            }

            issue.Priority = priority;
        }

        if (request.Status is not null)
        {
            issue.Status = request.Status.Trim().ToLowerInvariant() switch
            {
                "open" => ItemStatus.Open,
                "in-progress" or "inprogress" => ItemStatus.InProgress,
                "done" or "completed" => ItemStatus.Done,
                "blocked" => ItemStatus.Blocked,
                _ => throw new InvalidOperationException($"Unknown status '{request.Status}'. Valid values: open, in-progress, done, blocked.")
            };
        }

        if (request.ClearDependencies || request.DependsOnIssueIds is not null)
        {
            var dependsOn = request.ClearDependencies
                ? []
                : request.DependsOnIssueIds!
                    .Distinct()
                    .ToList();

            if (dependsOn.Contains(issue.Id))
            {
                throw new InvalidOperationException("An issue cannot depend on itself.");
            }

            var missing = dependsOn
                .Where(depId => state.Issues.All(existing => existing.Id != depId))
                .Distinct()
                .OrderBy(depId => depId)
                .ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException($"Unknown dependency issue id(s): {string.Join(", ", missing)}.");
            }

            issue.DependsOnIssueIds = dependsOn;
        }

        if (!string.IsNullOrWhiteSpace(request.NotesToAppend))
        {
            issue.Notes = string.IsNullOrWhiteSpace(issue.Notes)
                ? request.NotesToAppend.Trim()
                : $"{issue.Notes}\n{request.NotesToAppend.Trim()}";
        }

        if (issue.PipelineId is int pipelineId)
        {
            var pipeline = state.Pipelines.FirstOrDefault(item => item.Id == pipelineId);
            if (pipeline is not null)
            {
                pipeline.Area = issue.Area;
                pipeline.UpdatedAtUtc = _clock.UtcNow;
            }
        }

        EnsurePipelineAssignments(state);
        return issue;
    }
}
