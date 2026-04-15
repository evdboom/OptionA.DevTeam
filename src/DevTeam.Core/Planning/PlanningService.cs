namespace DevTeam.Core;

public sealed class PlanningService : IPlanningService
{
    private readonly ISystemClock _clock;

    public PlanningService(ISystemClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
    }

    public void ApprovePlan(WorkspaceState state, string note)
    {
        EnsureApprovedPlanningIssuesClosed(state);
        var hasArchitectWork = state.Issues.Any(issue =>
            !issue.IsPlanningIssue
            && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
            && issue.Status is ItemStatus.Open or ItemStatus.InProgress);

        state.Phase = hasArchitectWork ? WorkflowPhase.ArchitectPlanning : WorkflowPhase.Execution;
        RecordDecision(
            state,
            hasArchitectWork ? "Approved high-level plan — entering architect planning" : "Approved execution plan",
            string.IsNullOrWhiteSpace(note) ? "The current planning output is approved." : note.Trim(),
            "plan");
    }

    public void ApproveArchitectPlan(WorkspaceState state, string note)
    {
        EnsureApprovedPlanningIssuesClosed(state);
        state.Phase = WorkflowPhase.Execution;
        RecordDecision(
            state,
            "Approved detailed architect plan — entering execution",
            string.IsNullOrWhiteSpace(note) ? "The architect plan is approved for execution." : note.Trim(),
            "plan");
    }

    public void RecordPlanningFeedback(WorkspaceState state, string feedback)
    {
        var normalized = feedback.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Planning feedback cannot be empty.");
        }

        if (state.Phase == WorkflowPhase.ArchitectPlanning)
        {
            RecordDecision(
                state,
                "Architect plan feedback from user",
                normalized,
                "architect-plan-feedback");

            foreach (var architectIssue in state.Issues.Where(item =>
                         !item.IsPlanningIssue
                         && string.Equals(item.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
                         && item.Status is ItemStatus.Done or ItemStatus.Blocked))
            {
                architectIssue.Status = ItemStatus.Open;
            }

            return;
        }

        RecordDecision(
            state,
            "Planning feedback from user",
            normalized,
            "plan-feedback");

        var planningIssue = state.Issues.FirstOrDefault(item => item.IsPlanningIssue);
        if (planningIssue is not null && planningIssue.Status is ItemStatus.Done or ItemStatus.Blocked)
        {
            planningIssue.Status = ItemStatus.Open;
        }
    }

    public IReadOnlyList<string> EnsureBootstrapPlan(WorkspaceState state)
    {
        var created = new List<string>();
        if (state.ActiveGoal is null)
        {
            return created;
        }

        if (state.Roadmap.Count == 0)
        {
            state.Roadmap.Add(new RoadmapItem
            {
                Id = state.NextRoadmapId++,
                Title = "Plan the delivery strategy",
                Detail = $"Turn the goal into milestones, issues, role assignments, and open questions: {state.ActiveGoal.GoalText}",
                Priority = 100
            });
            created.Add("roadmap");
        }

        if (state.Issues.Count == 0)
        {
            var roadmapId = state.Roadmap.First().Id;
            var planningIssue = new IssueItem
            {
                Id = state.NextIssueId++,
                Title = "Run the planning session and split the work",
                Detail = "Generate the high-level strategy, identify what the architect needs to decide, and decompose the goal into broad milestones. Do not make technology or implementation choices — leave those to the architect.",
                IsPlanningIssue = true,
                RoleSlug = "planner",
                Priority = 100,
                RoadmapItemId = roadmapId
            };
            state.Issues.Add(planningIssue);
            state.Issues.Add(new IssueItem
            {
                Id = state.NextIssueId++,
                Title = "Design the technical approach and create execution issues",
                Detail = "Given the approved high-level plan, choose the technology stack, define the architecture, and break the work into concrete execution issues with clear dependencies.",
                RoleSlug = "architect",
                Priority = 90,
                RoadmapItemId = roadmapId,
                DependsOnIssueIds = [planningIssue.Id]
            });
            created.Add("issues");
        }

        return created;
    }

    public void EnsureApprovedPlanningIssuesClosed(WorkspaceState state)
    {
        if (state.Phase == WorkflowPhase.Planning)
        {
            return;
        }

        foreach (var planningIssue in state.Issues.Where(item => item.IsPlanningIssue && item.Status != ItemStatus.Done))
        {
            planningIssue.Status = ItemStatus.Done;
        }
    }

    private void RecordDecision(WorkspaceState state, string title, string detail, string source)
    {
        state.Decisions.Add(new DecisionRecord
        {
            Id = state.NextDecisionId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Source = source.Trim(),
            CreatedAtUtc = _clock.UtcNow
        });
    }
}
