namespace DevTeam.Core;

public sealed class DevTeamRuntime
{
    private static readonly Dictionary<string, string> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["engineer"] = "developer",
        ["software-engineer"] = "developer",
        ["coder"] = "developer",
        ["frontend-engineer"] = "frontend-developer",
        ["ui-engineer"] = "frontend-developer",
        ["backend-engineer"] = "backend-developer",
        ["api-engineer"] = "backend-developer",
        ["fullstack-engineer"] = "fullstack-developer",
        ["qa"] = "tester",
        ["qa-engineer"] = "tester",
        ["test-engineer"] = "tester"
    };

    public GoalState SetGoal(WorkspaceState state, string goalText)
    {
        state.Phase = WorkflowPhase.Planning;
        state.ActiveGoal = new GoalState
        {
            GoalText = goalText.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        RememberDecision(
            state,
            "Updated active goal",
            state.ActiveGoal.GoalText,
            "goal");
        return state.ActiveGoal;
    }

    public void ApprovePlan(WorkspaceState state, string note)
    {
        state.Phase = WorkflowPhase.Execution;
        RememberDecision(
            state,
            "Approved execution plan",
            string.IsNullOrWhiteSpace(note) ? "The current planning output is approved for execution." : note.Trim(),
            "plan");
    }

    public void RecordPlanningFeedback(WorkspaceState state, string feedback)
    {
        var normalized = feedback.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Planning feedback cannot be empty.");
        }

        RememberDecision(
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
        IEnumerable<int> dependsOn,
        string? area = null)
    {
        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Area = NormalizeArea(area),
            RoleSlug = ResolveRoleSlug(state, roleSlug),
            Priority = priority,
            RoadmapItemId = roadmapItemId,
            DependsOnIssueIds = dependsOn.Distinct().ToList()
        };
        state.Issues.Add(issue);
        return issue;
    }

    public IReadOnlyList<string> GetKnownRoleSlugs(WorkspaceState state)
    {
        return state.Roles
            .Select(role => role.Slug)
            .OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyDictionary<string, string> GetKnownRoleAliases(WorkspaceState state)
    {
        return RoleAliases
            .Where(pair => state.Roles.Any(role => string.Equals(role.Slug, pair.Value, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryResolveRoleSlug(WorkspaceState state, string roleSlug, out string resolvedRoleSlug)
    {
        return TryResolveRoleSlugInternal(state, roleSlug, out resolvedRoleSlug);
    }

    private static bool TryResolveRoleSlugInternal(WorkspaceState state, string roleSlug, out string resolvedRoleSlug)
    {
        var normalized = roleSlug.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            resolvedRoleSlug = "developer";
            return false;
        }

        var requestedKey = BuildRoleKey(normalized);
        var exact = state.Roles.FirstOrDefault(role =>
            string.Equals(role.Slug, normalized, StringComparison.OrdinalIgnoreCase)
            || BuildRoleKey(role.Slug) == requestedKey
            || BuildRoleKey(role.Name) == requestedKey);
        if (exact is not null)
        {
            resolvedRoleSlug = exact.Slug;
            return true;
        }

        var alias = RoleAliases
            .FirstOrDefault(pair => BuildRoleKey(pair.Key) == requestedKey)
            .Value;
        if (!string.IsNullOrWhiteSpace(alias))
        {
            var target = state.Roles.FirstOrDefault(role => string.Equals(role.Slug, alias, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                resolvedRoleSlug = target.Slug;
                return false;
            }
        }

        resolvedRoleSlug = "developer";
        return false;
    }

    public QuestionItem AddQuestion(WorkspaceState state, string text, bool blocking)
    {
        var normalized = text.Trim();
        var existing = state.Questions.FirstOrDefault(item =>
            item.Status == QuestionStatus.Open
            && item.IsBlocking == blocking
            && string.Equals(item.Text, normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var question = new QuestionItem
        {
            Id = state.NextQuestionId++,
            Text = normalized,
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
        RememberDecision(
            state,
            $"Answered question #{question.Id}",
            $"{question.Text}\n\nAnswer: {question.Answer}",
            "question");

        if (!state.Questions.Any(item => item.Status == QuestionStatus.Open && item.IsBlocking))
        {
            foreach (var issue in state.Issues.Where(item => item.Status == ItemStatus.Blocked))
            {
                issue.Status = ItemStatus.Open;
            }
        }
    }

    public IReadOnlyList<QuestionItem> AddQuestions(WorkspaceState state, IEnumerable<ProposedQuestion> questions)
    {
        return questions
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .Select(item => AddQuestion(state, item.Text, item.IsBlocking))
            .ToList();
    }

    public IReadOnlyList<IssueItem> AddGeneratedIssues(
        WorkspaceState state,
        int sourceIssueId,
        IEnumerable<GeneratedIssueProposal> issues)
    {
        var sourceIssue = state.Issues.FirstOrDefault(item => item.Id == sourceIssueId)
            ?? throw new InvalidOperationException($"Issue #{sourceIssueId} was not found.");
        var created = new List<IssueItem>();

        foreach (var proposal in issues.Where(item => !string.IsNullOrWhiteSpace(item.Title)))
        {
            var normalizedTitle = proposal.Title.Trim();
            var normalizedRole = ResolveRoleSlug(state, proposal.RoleSlug);
            var existing = state.Issues.FirstOrDefault(item =>
                string.Equals(item.Title, normalizedTitle, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.RoleSlug, normalizedRole, StringComparison.OrdinalIgnoreCase)
                && item.Status != ItemStatus.Done);
            if (existing is not null)
            {
                continue;
            }

            var issue = new IssueItem
            {
                Id = state.NextIssueId++,
                Title = normalizedTitle,
                Detail = proposal.Detail.Trim(),
                Area = NormalizeArea(proposal.Area),
                RoleSlug = normalizedRole,
                Priority = proposal.Priority,
                RoadmapItemId = sourceIssue.RoadmapItemId,
                DependsOnIssueIds = proposal.DependsOnIssueIds.Distinct().ToList()
            };
            state.Issues.Add(issue);
            created.Add(issue);
        }

        return created;
    }

    public LoopResult RunOnce(WorkspaceState state, int maxSubagents = 3)
    {
        var created = EnsureBootstrapPlan(state);
        var readyIssues = GetReadyIssues(state, maxSubagents);
        if (readyIssues.Count == 0)
        {
            if (state.Phase == WorkflowPhase.Planning
                && state.Issues.Any(issue => issue.IsPlanningIssue && issue.Status == ItemStatus.Done))
            {
                return new LoopResult
                {
                    State = HasBlockingQuestions(state) ? "waiting-for-user" : "awaiting-plan-approval",
                    Created = created
                };
            }

            return new LoopResult
            {
                State = HasBlockingQuestions(state) ? "waiting-for-user" : "idle",
                Created = created
            };
        }

        var queued = new List<QueuedRunInfo>();
        foreach (var issue in readyIssues)
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
                Area = issue.Area,
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

    public void CompleteRun(
        WorkspaceState state,
        int runId,
        string outcome,
        string summary,
        IEnumerable<string>? superpowersUsed = null,
        IEnumerable<string>? toolsUsed = null)
    {
        var run = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId)
            ?? throw new InvalidOperationException($"Issue #{run.IssueId} was not found.");

        run.Summary = summary.Trim();
        run.SuperpowersUsed = superpowersUsed?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        run.ToolsUsed = toolsUsed?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
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

        RememberDecision(
            state,
            $"Run #{run.Id} {run.Status}",
            summary.Trim(),
            "run",
            issue.Id,
            run.Id,
            run.SessionId);
    }

    public void StartRun(WorkspaceState state, int runId, string sessionId)
    {
        var run = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        run.Status = AgentRunStatus.Running;
        run.SessionId = sessionId.Trim();
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
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
                ["decisions"] = state.Decisions.Count,
                ["roles"] = state.Roles.Count,
                ["superpowers"] = state.Superpowers.Count
            },
            Budget = state.Budget,
            QueuedRuns = state.AgentRuns.Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running).ToList(),
            OpenQuestions = state.Questions.Where(question => question.Status == QuestionStatus.Open).ToList(),
            RecentDecisions = state.Decisions
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(5)
                .ToList()
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
                IsPlanningIssue = true,
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

    private static List<IssueItem> GetReadyIssues(WorkspaceState state, int maxSubagents)
    {
        var activeRuns = state.AgentRuns
            .Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running)
            .ToList();
        var queuedIssueIds = activeRuns
            .Select(run => run.IssueId)
            .ToHashSet();
        var reservedAreas = activeRuns
            .Select(run => state.Issues.FirstOrDefault(issue => issue.Id == run.IssueId)?.Area)
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ready = state.Issues
            .Where(issue => issue.Status == ItemStatus.Open)
            .Where(issue => !queuedIssueIds.Contains(issue.Id))
            .Where(issue => state.Phase == WorkflowPhase.Planning ? issue.IsPlanningIssue : !issue.IsPlanningIssue)
            .Where(issue => issue.DependsOnIssueIds.All(depId => state.Issues.Any(dep => dep.Id == depId && dep.Status == ItemStatus.Done)))
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Id)
            .ToList();

        var selected = new List<IssueItem>();
        foreach (var issue in ready)
        {
            if (selected.Count >= Math.Max(1, maxSubagents))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(issue.Area) && reservedAreas.Contains(issue.Area))
            {
                continue;
            }

            selected.Add(issue);
            if (!string.IsNullOrWhiteSpace(issue.Area))
            {
                reservedAreas.Add(issue.Area);
            }
        }

        return selected;
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

    private static DecisionRecord RememberDecision(
        WorkspaceState state,
        string title,
        string detail,
        string source,
        int? issueId = null,
        int? runId = null,
        string? sessionId = null)
    {
        var record = new DecisionRecord
        {
            Id = state.NextDecisionId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Source = source.Trim(),
            IssueId = issueId,
            RunId = runId,
            SessionId = sessionId?.Trim() ?? "",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        state.Decisions.Add(record);
        return record;
    }

    private static string ResolveRoleSlug(WorkspaceState state, string roleSlug)
    {
        TryResolveRoleSlugInternal(state, roleSlug, out var resolvedRoleSlug);
        return resolvedRoleSlug;
    }

    private static string NormalizeArea(string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            return "";
        }

        var chars = area.Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    private static string BuildRoleKey(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}

