namespace DevTeam.Core;

public sealed class DevTeamRuntime
{
    public GoalState SetGoal(WorkspaceState state, string goalText)
    {
        state.ActiveGoal = new GoalState
        {
            GoalText = goalText.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        return state.ActiveGoal;
    }

    public RoadmapItem AddRoadmapItem(WorkspaceState state, string title, string detail, int priority)
    {
        var item = new RoadmapItem
        {
            Id = state.NextRoadmapId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Priority = priority
        };
        state.Roadmap.Add(item);
        return item;
    }

    public IssueItem AddIssue(
        WorkspaceState state,
        string title,
        string detail,
        string roleSlug,
        int priority,
        int? roadmapItemId,
        IEnumerable<int> dependsOn)
    {
        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            RoleSlug = roleSlug.Trim(),
            Priority = priority,
            RoadmapItemId = roadmapItemId,
            DependsOnIssueIds = dependsOn.Distinct().ToList()
        };
        state.Issues.Add(issue);
        return issue;
    }

    public QuestionItem AddQuestion(WorkspaceState state, string text, bool blocking)
    {
        var question = new QuestionItem
        {
            Id = state.NextQuestionId++,
            Text = text.Trim(),
            IsBlocking = blocking
        };
        state.Questions.Add(question);
        return question;
    }

    public void AnswerQuestion(WorkspaceState state, int questionId, string answer)
    {
        var question = state.Questions.FirstOrDefault(item => item.Id == questionId)
            ?? throw new InvalidOperationException($"Question #{questionId} was not found.");
        question.Answer = answer.Trim();
        question.Status = QuestionStatus.Answered;
    }

    public LoopResult RunOnce(WorkspaceState state, int maxSubagents = 3)
    {
        var created = EnsureBootstrapPlan(state);
        var readyIssues = GetReadyIssues(state);
        if (readyIssues.Count == 0)
        {
            return new LoopResult
            {
                State = HasBlockingQuestions(state) ? "waiting-for-user" : "idle",
                Created = created
            };
        }

        var queued = new List<QueuedRunInfo>();
        foreach (var issue in readyIssues.Take(Math.Max(1, maxSubagents)))
        {
            var model = SelectModelForRole(state, issue.RoleSlug);
            var run = new AgentRun
            {
                Id = state.NextRunId++,
                IssueId = issue.Id,
                RoleSlug = issue.RoleSlug,
                ModelName = model.Name,
                Status = AgentRunStatus.Queued,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            issue.Status = ItemStatus.InProgress;
            state.AgentRuns.Add(run);
            CommitCredits(state, model);
            queued.Add(new QueuedRunInfo
            {
                RunId = run.Id,
                IssueId = issue.Id,
                Title = issue.Title,
                RoleSlug = issue.RoleSlug,
                ModelName = model.Name
            });
        }

        return new LoopResult
        {
            State = "queued",
            Created = created,
            QueuedRuns = queued
        };
    }

    public void CompleteRun(WorkspaceState state, int runId, string outcome, string summary)
    {
        var run = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId)
            ?? throw new InvalidOperationException($"Issue #{run.IssueId} was not found.");

        run.Summary = summary.Trim();
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
        switch (outcome.Trim().ToLowerInvariant())
        {
            case "completed":
                run.Status = AgentRunStatus.Completed;
                issue.Status = ItemStatus.Done;
                break;
            case "failed":
                run.Status = AgentRunStatus.Failed;
                issue.Status = ItemStatus.Open;
                break;
            case "blocked":
                run.Status = AgentRunStatus.Blocked;
                issue.Status = ItemStatus.Blocked;
                break;
            default:
                throw new InvalidOperationException("Outcome must be one of: completed, failed, blocked.");
        }
    }

    public StatusReport BuildStatusReport(WorkspaceState state)
    {
        return new StatusReport
        {
            Counts = new Dictionary<string, int>
            {
                ["roadmap"] = state.Roadmap.Count,
                ["issues"] = state.Issues.Count,
                ["questions"] = state.Questions.Count,
                ["runs"] = state.AgentRuns.Count,
                ["roles"] = state.Roles.Count,
                ["superpowers"] = state.Superpowers.Count
            },
            Budget = state.Budget,
            QueuedRuns = state.AgentRuns.Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running).ToList(),
            OpenQuestions = state.Questions.Where(question => question.Status == QuestionStatus.Open).ToList()
        };
    }

    private static List<string> EnsureBootstrapPlan(WorkspaceState state)
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
                Detail = "Generate the first roadmap detail, identify missing information, and decompose the goal into small issues.",
                RoleSlug = "orchestrator",
                Priority = 100,
                RoadmapItemId = roadmapId
            };
            state.Issues.Add(planningIssue);
            state.Issues.Add(new IssueItem
            {
                Id = state.NextIssueId++,
                Title = "Draft the initial runtime architecture",
                Detail = "Design execution phases, data model, tool boundaries, and integration seams for the dev-team runtime.",
                RoleSlug = "architect",
                Priority = 90,
                RoadmapItemId = roadmapId,
                DependsOnIssueIds = [planningIssue.Id]
            });
            created.Add("issues");
        }

        return created;
    }

    private static List<IssueItem> GetReadyIssues(WorkspaceState state)
    {
        var queuedIssueIds = state.AgentRuns
            .Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running)
            .Select(run => run.IssueId)
            .ToHashSet();

        return state.Issues
            .Where(issue => issue.Status == ItemStatus.Open)
            .Where(issue => !queuedIssueIds.Contains(issue.Id))
            .Where(issue => issue.DependsOnIssueIds.All(depId => state.Issues.Any(dep => dep.Id == depId && dep.Status == ItemStatus.Done)))
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Id)
            .ToList();
    }

    private static bool HasBlockingQuestions(WorkspaceState state) =>
        state.Questions.Any(question => question.Status == QuestionStatus.Open && question.IsBlocking);

    private static ModelDefinition SelectModelForRole(WorkspaceState state, string roleSlug)
    {
        var policy = SeedData.GetPolicy(state, roleSlug);
        var defaultModel = state.Models.FirstOrDefault(model => model.IsDefault) ?? new ModelDefinition
        {
            Name = "gpt-5-mini",
            Cost = 0
        };

        var primary = state.Models.FirstOrDefault(model => string.Equals(model.Name, policy.PrimaryModel, StringComparison.OrdinalIgnoreCase))
            ?? defaultModel;
        var fallback = state.Models.FirstOrDefault(model => string.Equals(model.Name, policy.FallbackModel, StringComparison.OrdinalIgnoreCase))
            ?? defaultModel;

        var totalAfter = state.Budget.CreditsCommitted + primary.Cost;
        var premiumAfter = state.Budget.PremiumCreditsCommitted + (primary.IsPremium ? primary.Cost : 0);
        var premiumAllowed = !primary.IsPremium || policy.AllowPremium;

        if (totalAfter <= state.Budget.TotalCreditCap
            && premiumAfter <= state.Budget.PremiumCreditCap
            && premiumAllowed)
        {
            return primary;
        }

        return fallback;
    }

    private static void CommitCredits(WorkspaceState state, ModelDefinition model)
    {
        state.Budget.CreditsCommitted += model.Cost;
        if (model.IsPremium)
        {
            state.Budget.PremiumCreditsCommitted += model.Cost;
        }
    }
}

