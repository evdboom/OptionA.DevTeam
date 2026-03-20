using System.Security.Cryptography;
using System.Text;

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

    public void SetMode(WorkspaceState state, string modeSlug)
    {
        var normalized = modeSlug.Trim();
        var mode = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown mode '{modeSlug}'. Valid modes: {string.Join(", ", state.Modes.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))}");
        state.Runtime.ActiveModeSlug = mode.Slug;
        state.Runtime.DefaultPipelineRoles = GetDefaultPipelineRolesForMode(mode.Slug);
        RememberDecision(state, "Updated active mode", mode.Slug, "mode");
    }

    public ModeDefinition GetActiveMode(WorkspaceState state)
    {
        var active = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, state.Runtime.ActiveModeSlug, StringComparison.OrdinalIgnoreCase));
        return active ?? state.Modes.FirstOrDefault() ?? new ModeDefinition
        {
            Slug = "develop",
            Name = "Develop",
            Body = "# Mode: Develop"
        };
    }

    public IReadOnlyList<string> GetKnownModeSlugs(WorkspaceState state) =>
        state.Modes
            .Select(mode => mode.Slug)
            .OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        string? area = null,
        string? familyKey = null,
        int? parentIssueId = null,
        int? pipelineId = null,
        int? pipelineStageIndex = null)
        => CreateIssue(state, title, detail, roleSlug, priority, roadmapItemId, dependsOn, area, familyKey, parentIssueId, pipelineId, pipelineStageIndex);

    public WorkspaceSnapshot BuildWorkspaceSnapshot(WorkspaceState state)
    {
        EnsurePipelineAssignments(state);
        return new WorkspaceSnapshot
        {
            RepoRoot = state.RepoRoot,
            Phase = state.Phase,
            ActiveGoal = state.ActiveGoal,
            Runtime = state.Runtime,
            Modes = state.Modes.OrderBy(item => item.Slug).ToList(),
            Issues = state.Issues.OrderBy(item => item.Id).ToList(),
            Questions = state.Questions.OrderBy(item => item.Id).ToList(),
            AgentSessions = state.AgentSessions.OrderBy(item => item.ScopeKey).ToList(),
            Decisions = state.Decisions.OrderByDescending(item => item.CreatedAtUtc).Take(20).ToList(),
            Pipelines = state.Pipelines.OrderBy(item => item.Id).ToList()
        };
    }

    public IReadOnlyList<IssueItem> GetReadyIssuesPreview(WorkspaceState state, int maxSubagents)
    {
        EnsurePipelineAssignments(state);
        return GetReadyIssues(state, maxSubagents);
    }

    public DecisionRecord RecordDecision(
        WorkspaceState state,
        string title,
        string detail,
        string source,
        int? issueId = null,
        int? runId = null,
        string? sessionId = null) =>
        RememberDecision(state, title, detail, source, issueId, runId, sessionId);

    public void MergeWorkspaceAdditions(WorkspaceState state, WorkspaceState externalState)
    {
        foreach (var question in externalState.Questions.Where(item => state.Questions.All(existing => existing.Id != item.Id)))
        {
            state.Questions.Add(question);
        }

        foreach (var issue in externalState.Issues.Where(item => state.Issues.All(existing => existing.Id != item.Id)))
        {
            state.Issues.Add(issue);
        }

        foreach (var session in externalState.AgentSessions.Where(item => state.AgentSessions.All(existing => !string.Equals(existing.ScopeKey, item.ScopeKey, StringComparison.OrdinalIgnoreCase))))
        {
            state.AgentSessions.Add(session);
        }

        foreach (var decision in externalState.Decisions.Where(item => state.Decisions.All(existing => existing.Id != item.Id)))
        {
            state.Decisions.Add(decision);
        }

        foreach (var pipeline in externalState.Pipelines.Where(item => state.Pipelines.All(existing => existing.Id != item.Id)))
        {
            state.Pipelines.Add(pipeline);
        }

        state.NextIssueId = Math.Max(state.NextIssueId, externalState.NextIssueId);
        state.NextQuestionId = Math.Max(state.NextQuestionId, externalState.NextQuestionId);
        state.NextDecisionId = Math.Max(state.NextDecisionId, externalState.NextDecisionId);
        state.NextPipelineId = Math.Max(state.NextPipelineId, externalState.NextPipelineId);
    }

    public AgentSession GetOrCreateAgentSession(WorkspaceState state, int runId)
    {
        var run = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId)
            ?? throw new InvalidOperationException($"Issue #{run.IssueId} was not found.");
        var scope = DescribeSessionScope(issue);
        var binding = state.AgentSessions.FirstOrDefault(item => string.Equals(item.ScopeKey, scope.ScopeKey, StringComparison.OrdinalIgnoreCase));
        if (binding is null)
        {
            binding = new AgentSession
            {
                ScopeKey = scope.ScopeKey,
                ScopeKind = scope.ScopeKind,
                RoleSlug = issue.RoleSlug,
                IssueId = scope.IssueId,
                PipelineId = scope.PipelineId,
                SessionId = BuildSessionId(state.RepoRoot, scope.ScopeKey, issue.RoleSlug),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            state.AgentSessions.Add(binding);
        }

        binding.RoleSlug = issue.RoleSlug;
        binding.IssueId = scope.IssueId;
        binding.PipelineId = scope.PipelineId;
        binding.LastRunId = runId;
        binding.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return binding;
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
                FamilyKey = NormalizeFamilyKey("", normalizedTitle, proposal.Area),
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
        EnsurePipelineAssignments(state);
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
                AdvancePipelineAfterCompletion(state, issue);
                break;
            case "failed":
                run.Status = AgentRunStatus.Failed;
                issue.Status = ItemStatus.Open;
                UpdatePipelineStatus(state, issue, PipelineStatus.Open, issue.Id);
                break;
            case "blocked":
                run.Status = AgentRunStatus.Blocked;
                issue.Status = ItemStatus.Blocked;
                UpdatePipelineStatus(state, issue, PipelineStatus.Blocked, issue.Id);
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
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId);
        if (issue is not null)
        {
            UpdatePipelineStatus(state, issue, PipelineStatus.Running, issue.Id);
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
        var reservedPipelineIds = activeRuns
            .Select(run => state.Issues.FirstOrDefault(issue => issue.Id == run.IssueId)?.PipelineId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        var ready = state.Issues
            .Where(issue => issue.Status == ItemStatus.Open)
            .Where(issue => !queuedIssueIds.Contains(issue.Id))
            .Where(issue => state.Phase == WorkflowPhase.Planning ? issue.IsPlanningIssue : !issue.IsPlanningIssue)
            .Where(issue => issue.DependsOnIssueIds.All(depId => state.Issues.Any(dep => dep.Id == depId && dep.Status == ItemStatus.Done)))
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Id)
            .ToList();

        foreach (var issue in ready)
        {
            EnsurePipelineForIssue(state, issue);
        }

        var readyLeads = ready
            .GroupBy(issue => issue.PipelineId ?? issue.Id)
            .Select(group => group
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.Id)
                .First())
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Id)
            .ToList();

        var desiredConcurrency = DeterminePipelineConcurrency(state, readyLeads, maxSubagents);

        var selected = new List<IssueItem>();
        foreach (var issue in readyLeads)
        {
            if (selected.Count >= desiredConcurrency)
            {
                break;
            }

            if (issue.PipelineId is int pipelineId && reservedPipelineIds.Contains(pipelineId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(issue.Area) && reservedAreas.Contains(issue.Area))
            {
                continue;
            }

            selected.Add(issue);
            if (issue.PipelineId is int selectedPipelineId)
            {
                reservedPipelineIds.Add(selectedPipelineId);
            }
            if (!string.IsNullOrWhiteSpace(issue.Area))
            {
                reservedAreas.Add(issue.Area);
            }
        }

        return selected;
    }

    private static bool HasBlockingQuestions(WorkspaceState state) =>
        state.Questions.Any(question => question.Status == QuestionStatus.Open && question.IsBlocking);

    private static void EnsurePipelineAssignments(WorkspaceState state)
    {
        foreach (var issue in state.Issues.Where(item => !item.IsPlanningIssue))
        {
            EnsurePipelineForIssue(state, issue);
        }
    }

    private static void EnsurePipelineForIssue(WorkspaceState state, IssueItem issue)
    {
        if (issue.IsPlanningIssue || !state.Runtime.PipelineSchedulingEnabled)
        {
            return;
        }

        issue.FamilyKey = NormalizeFamilyKey(issue.FamilyKey, issue.Title, issue.Area);
        if (issue.PipelineId is not null && state.Pipelines.Any(item => item.Id == issue.PipelineId.Value))
        {
            return;
        }

        var sequence = BuildPipelineSequence(state, issue.RoleSlug);
        var pipeline = new PipelineState
        {
            Id = state.NextPipelineId++,
            RootIssueId = issue.Id,
            FamilyKey = issue.FamilyKey,
            Area = issue.Area,
            RoleSequence = sequence,
            IssueIds = [issue.Id],
            ActiveIssueId = issue.Status is ItemStatus.Done ? null : issue.Id,
            Status = issue.Status switch
            {
                ItemStatus.Blocked => PipelineStatus.Blocked,
                ItemStatus.InProgress => PipelineStatus.Running,
                ItemStatus.Done when sequence.Count <= 1 => PipelineStatus.Completed,
                _ => PipelineStatus.Open
            },
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        issue.PipelineId = pipeline.Id;
        issue.PipelineStageIndex ??= 0;
        state.Pipelines.Add(pipeline);
    }

    private static List<string> BuildPipelineSequence(WorkspaceState state, string rootRoleSlug)
    {
        var normalized = ResolveRoleSlug(state, rootRoleSlug);
        if (state.Runtime.DefaultPipelineRoles.Count > 0
            && state.Runtime.DefaultPipelineRoles.Any(role => string.Equals(role, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return state.Runtime.DefaultPipelineRoles
                .Where(role => state.Roles.Any(item => string.Equals(item.Slug, role, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        List<string> sequence = normalized switch
        {
            "architect" => ["architect", "developer", "tester"],
            "developer" or "backend-developer" or "frontend-developer" or "fullstack-developer" => [normalized, "tester"],
            _ => [normalized]
        };

        return sequence
            .Where(role => state.Roles.Any(item => string.Equals(item.Slug, role, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetDefaultPipelineRolesForMode(string modeSlug) =>
        modeSlug.Trim().ToLowerInvariant() switch
        {
            "creative-writing" => ["architect", "developer", "reviewer"],
            _ => ["architect", "developer", "tester"]
        };

    private static int DeterminePipelineConcurrency(WorkspaceState state, IReadOnlyList<IssueItem> readyLeads, int maxSubagents)
    {
        if (readyLeads.Count == 0)
        {
            return 0;
        }

        if (state.Phase == WorkflowPhase.Planning || maxSubagents <= 1)
        {
            return 1;
        }

        var desired = Math.Min(Math.Max(1, maxSubagents), readyLeads.Count);
        if (readyLeads.Count == 1)
        {
            return 1;
        }

        var topPriority = readyLeads[0].Priority;
        var secondPriority = readyLeads[1].Priority;
        if (topPriority - secondPriority >= 20)
        {
            return 1;
        }

        return desired;
    }

    private static void AdvancePipelineAfterCompletion(WorkspaceState state, IssueItem issue)
    {
        if (issue.PipelineId is null)
        {
            return;
        }

        var pipeline = state.Pipelines.FirstOrDefault(item => item.Id == issue.PipelineId.Value);
        if (pipeline is null)
        {
            return;
        }

        var currentStage = issue.PipelineStageIndex ?? 0;
        if (currentStage >= pipeline.RoleSequence.Count - 1)
        {
            pipeline.ActiveIssueId = null;
            pipeline.Status = PipelineStatus.Completed;
            pipeline.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return;
        }

        var nextStageIndex = currentStage + 1;
        var nextIssue = state.Issues.FirstOrDefault(item =>
            item.PipelineId == pipeline.Id && item.PipelineStageIndex == nextStageIndex);
        if (nextIssue is null)
        {
            nextIssue = CreateIssue(
                state,
                issue.Title,
                issue.Detail,
                pipeline.RoleSequence[nextStageIndex],
                Math.Max(1, issue.Priority - 5),
                issue.RoadmapItemId,
                [issue.Id],
                issue.Area,
                issue.FamilyKey,
                issue.Id,
                pipeline.Id,
                nextStageIndex);
            pipeline.IssueIds.Add(nextIssue.Id);
        }

        pipeline.ActiveIssueId = nextIssue.Id;
        pipeline.Status = nextIssue.Status == ItemStatus.Blocked ? PipelineStatus.Blocked : PipelineStatus.Open;
        pipeline.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void UpdatePipelineStatus(WorkspaceState state, IssueItem issue, PipelineStatus status, int? activeIssueId)
    {
        if (issue.PipelineId is null)
        {
            return;
        }

        var pipeline = state.Pipelines.FirstOrDefault(item => item.Id == issue.PipelineId.Value);
        if (pipeline is null)
        {
            return;
        }

        pipeline.Status = status;
        pipeline.ActiveIssueId = activeIssueId;
        pipeline.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

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

    private static (string ScopeKey, string ScopeKind, int? IssueId, int? PipelineId) DescribeSessionScope(IssueItem issue)
    {
        if (issue.IsPlanningIssue || string.Equals(issue.RoleSlug, "orchestrator", StringComparison.OrdinalIgnoreCase))
        {
            return ("workspace:orchestrator", "workspace", issue.Id, null);
        }

        if (issue.PipelineId is not null)
        {
            return ($"pipeline:{issue.PipelineId.Value}:role:{issue.RoleSlug}", "pipeline-role", null, issue.PipelineId);
        }

        return ($"issue:{issue.Id}:role:{issue.RoleSlug}", "issue", issue.Id, null);
    }

    private static string BuildSessionId(string repoRoot, string scopeKey, string roleSlug)
    {
        var normalizedRole = NormalizeArea(roleSlug);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{Path.GetFullPath(repoRoot)}|{scopeKey}")))
            .ToLowerInvariant()[..12];
        return $"devteam-{normalizedRole}-{hash}";
    }

    private static string NormalizeFamilyKey(string? familyKey, string title, string? area)
    {
        if (!string.IsNullOrWhiteSpace(familyKey))
        {
            return BuildRoleKey(familyKey);
        }

        if (!string.IsNullOrWhiteSpace(area))
        {
            return NormalizeArea(area);
        }

        return BuildRoleKey(title);
    }

    private static IssueItem CreateIssue(
        WorkspaceState state,
        string title,
        string detail,
        string roleSlug,
        int priority,
        int? roadmapItemId,
        IEnumerable<int> dependsOn,
        string? area,
        string? familyKey,
        int? parentIssueId,
        int? pipelineId,
        int? pipelineStageIndex)
    {
        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Area = NormalizeArea(area),
            FamilyKey = NormalizeFamilyKey(familyKey, title, area),
            RoleSlug = ResolveRoleSlug(state, roleSlug),
            Priority = priority,
            RoadmapItemId = roadmapItemId,
            DependsOnIssueIds = dependsOn.Distinct().ToList(),
            ParentIssueId = parentIssueId,
            PipelineId = pipelineId,
            PipelineStageIndex = pipelineStageIndex
        };
        state.Issues.Add(issue);
        return issue;
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

