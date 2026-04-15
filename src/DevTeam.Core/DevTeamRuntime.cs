namespace DevTeam.Core;

public class DevTeamRuntime
{
    private readonly ISystemClock _clock;
    private readonly IIssueService _issueService;
    private readonly IQuestionService _questionService;
    private readonly IRoadmapService _roadmapService;
    private readonly IBudgetService _budgetService;
    private readonly IPlanningService _planningService;
    private readonly ISessionManager _sessionManager;

    public DevTeamRuntime(
        ISystemClock? clock = null,
        IIssueService? issueService = null,
        IQuestionService? questionService = null,
        IRoadmapService? roadmapService = null,
        IBudgetService? budgetService = null,
        IPlanningService? planningService = null,
        ISessionManager? sessionManager = null)
    {
        _clock = clock ?? new SystemClock();
        _issueService = issueService ?? new IssueService(_clock);
        _questionService = questionService ?? new QuestionService(_clock);
        _roadmapService = roadmapService ?? new RoadmapService();
        _budgetService = budgetService ?? new BudgetService();
        _planningService = planningService ?? new PlanningService(_clock);
        _sessionManager = sessionManager ?? new SessionManager(_clock);
    }

    public GoalState SetGoal(WorkspaceState state, string goalText)
    {
        state.Phase = WorkflowPhase.Planning;
        state.ActiveGoal = new GoalState
        {
            GoalText = goalText.Trim(),
            UpdatedAtUtc = _clock.UtcNow
        };
        RememberDecision(state, "Updated active goal", state.ActiveGoal.GoalText, "goal");
        return state.ActiveGoal;
    }

    public void SetMode(WorkspaceState state, string modeSlug)
    {
        var normalized = modeSlug.Trim();
        var mode = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown mode '{modeSlug}'. Valid modes: {string.Join(", ", state.Modes.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))}");
        state.Runtime.ActiveModeSlug = mode.Slug;
        state.Runtime.DefaultPipelineRoles = mode.Slug.Trim().ToLowerInvariant() switch
        {
            "creative-writing" => ["architect", "developer", "reviewer"],
            _ => ["architect", "developer", "tester"]
        };
        state.Runtime.AutoApproveEnabled = string.Equals(mode.Slug, "autopilot", StringComparison.OrdinalIgnoreCase);
        RememberDecision(state, "Updated active mode", mode.Slug, "mode");
    }

    public void SetKeepAwake(WorkspaceState state, bool enabled)
    {
        state.Runtime.KeepAwakeEnabled = enabled;
        RememberDecision(state, "Updated keep-awake setting", enabled ? "enabled" : "disabled", "runtime");
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

    public void ApprovePlan(WorkspaceState state, string note) =>
        _planningService.ApprovePlan(state, note);

    public void ApproveArchitectPlan(WorkspaceState state, string note) =>
        _planningService.ApproveArchitectPlan(state, note);

    public void SetAutoApprove(WorkspaceState state, bool enabled)
    {
        state.Runtime.AutoApproveEnabled = enabled;
        RememberDecision(state, "Updated auto-approve setting", enabled ? "enabled" : "disabled", "runtime");
    }

    public void SetDefaultMaxIterations(WorkspaceState state, int value)
    {
        if (value < 1) throw new InvalidOperationException("max-iterations must be at least 1.");
        state.Runtime.DefaultMaxIterations = value;
        RememberDecision(state, "Updated default max-iterations", value.ToString(), "runtime");
    }

    public void SetDefaultMaxSubagents(WorkspaceState state, int value)
    {
        if (value < 1) throw new InvalidOperationException("max-subagents must be at least 1.");
        state.Runtime.DefaultMaxSubagents = value;
        RememberDecision(state, "Updated default max-subagents", value.ToString(), "runtime");
    }

    public void RecordPlanningFeedback(WorkspaceState state, string feedback) =>
        _planningService.RecordPlanningFeedback(state, feedback);

    public RoadmapItem AddRoadmapItem(WorkspaceState state, string title, string detail, int priority) =>
        _roadmapService.AddRoadmapItem(state, title, detail, priority);

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
        int? pipelineStageIndex = null,
        int? complexityHint = null)
        => _issueService.AddIssue(state, title, detail, roleSlug, priority, roadmapItemId, dependsOn,
            area, familyKey, parentIssueId, pipelineId, pipelineStageIndex, complexityHint);

    public IssueItem UpdateIssueStatus(WorkspaceState state, int issueId, string status, string? notes = null)
    {
        var issue = state.Issues.FirstOrDefault(i => i.Id == issueId)
            ?? throw new InvalidOperationException($"Issue #{issueId} not found.");

        issue.Status = status.ToLowerInvariant() switch
        {
            "open" => ItemStatus.Open,
            "in-progress" or "inprogress" => ItemStatus.InProgress,
            "done" or "completed" => ItemStatus.Done,
            "blocked" => ItemStatus.Blocked,
            _ => throw new InvalidOperationException($"Unknown status '{status}'. Valid values: open, in-progress, done, blocked.")
        };

        if (!string.IsNullOrWhiteSpace(notes))
        {
            issue.Notes = string.IsNullOrWhiteSpace(issue.Notes)
                ? notes
                : $"{issue.Notes}\n{notes}";
        }

        return issue;
    }

    public WorkspaceSnapshot BuildWorkspaceSnapshot(WorkspaceState state)
    {
        _issueService.EnsurePipelineAssignments(state);
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
            ExecutionSelection = state.ExecutionSelection,
            Decisions = state.Decisions.OrderByDescending(item => item.CreatedAtUtc).Take(20).ToList(),
            Pipelines = state.Pipelines.OrderBy(item => item.Id).ToList()
        };
    }

    public IReadOnlyList<IssueItem> GetReadyIssuesPreview(WorkspaceState state, int maxSubagents)
    {
        _issueService.EnsurePipelineAssignments(state);
        return _issueService.GetReadyIssues(state, maxSubagents);
    }

    public IReadOnlyList<string> PrepareForLoop(WorkspaceState state)
    {
        _planningService.EnsureApprovedPlanningIssuesClosed(state);
        var created = _planningService.EnsureBootstrapPlan(state);
        _issueService.EnsurePipelineAssignments(state);
        return created;
    }

    public string GetLoopStateWhenNoReadyWork(WorkspaceState state)
    {
        if (state.Phase == WorkflowPhase.Planning
            && state.Issues.Any(issue => issue.IsPlanningIssue && issue.Status == ItemStatus.Done))
        {
            return _issueService.HasBlockingQuestions(state) ? "waiting-for-user" : "awaiting-plan-approval";
        }

        if (state.Phase == WorkflowPhase.ArchitectPlanning
            && state.Issues.Where(issue => string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase) && !issue.IsPlanningIssue).All(issue => issue.Status == ItemStatus.Done))
        {
            return _issueService.HasBlockingQuestions(state) ? "waiting-for-user" : "awaiting-architect-approval";
        }

        return _issueService.HasBlockingQuestions(state) ? "waiting-for-user" : "idle";
    }

    public IReadOnlyList<IssueItem> GetExecutionCandidatesPreview(WorkspaceState state)
    {
        _issueService.EnsurePipelineAssignments(state);
        return _issueService.GetReadyIssueCandidates(state);
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

        foreach (var externalQuestion in externalState.Questions.Where(q => q.Status == QuestionStatus.Answered))
        {
            var existing = state.Questions.FirstOrDefault(q => q.Id == externalQuestion.Id);
            if (existing is { Status: QuestionStatus.Open })
            {
                existing.Status = externalQuestion.Status;
                existing.Answer = externalQuestion.Answer;
            }
        }

        foreach (var issue in externalState.Issues.Where(item => state.Issues.All(existing => existing.Id != item.Id)))
        {
            state.Issues.Add(issue);
        }

        foreach (var session in externalState.AgentSessions.Where(item => state.AgentSessions.All(existing => !string.Equals(existing.ScopeKey, item.ScopeKey, StringComparison.OrdinalIgnoreCase))))
        {
            state.AgentSessions.Add(session);
        }

        if (externalState.ExecutionSelection.SelectedIssueIds.Count > 0 || !string.IsNullOrWhiteSpace(externalState.ExecutionSelection.Rationale))
        {
            state.ExecutionSelection = externalState.ExecutionSelection;
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

    public AgentSession GetOrCreateAgentSession(WorkspaceState state, int runId) =>
        _sessionManager.GetOrCreateAgentSession(state, runId);

    public AgentSession GetOrCreateExecutionOrchestratorSession(WorkspaceState state) =>
        _sessionManager.GetOrCreateExecutionOrchestratorSession(state);

    public void ClearExecutionSelection(WorkspaceState state)
    {
        state.ExecutionSelection = new ExecutionSelectionState();
    }

    public ExecutionSelectionState SetExecutionSelection(
        WorkspaceState state,
        IEnumerable<int> issueIds,
        string rationale,
        string? sessionId = null,
        int maxSubagents = 4)
    {
        _issueService.EnsurePipelineAssignments(state);
        var candidates = _issueService.GetReadyIssueCandidates(state);
        var candidateMap = candidates.ToDictionary(item => item.Id);
        var selected = issueIds.Distinct().ToList();
        if (selected.Count > Math.Max(1, maxSubagents))
        {
            throw new InvalidOperationException($"Execution batch can select at most {Math.Max(1, maxSubagents)} issue(s).");
        }

        foreach (var issueId in selected)
        {
            if (!candidateMap.ContainsKey(issueId))
            {
                throw new InvalidOperationException($"Issue #{issueId} is not a ready execution candidate.");
            }
        }

        var selectedIssues = selected.Select(id => candidateMap[id]).ToList();
        var duplicateAreas = selectedIssues
            .Where(item => !string.IsNullOrWhiteSpace(item.Area))
            .GroupBy(item => item.Area, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAreas is not null)
        {
            throw new InvalidOperationException($"Execution batch cannot select multiple issues in area '{duplicateAreas.Key}'.");
        }

        var duplicatePipelines = selectedIssues
            .Where(item => item.PipelineId is not null)
            .GroupBy(item => item.PipelineId!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePipelines is not null)
        {
            throw new InvalidOperationException($"Execution batch cannot select multiple leads from pipeline #{duplicatePipelines.Key}.");
        }

        state.ExecutionSelection = new ExecutionSelectionState
        {
            SelectedIssueIds = selected,
            Rationale = rationale.Trim(),
            SessionId = sessionId?.Trim() ?? "",
            UpdatedAtUtc = _clock.UtcNow
        };
        RememberDecision(
            state,
            "Selected execution batch",
            state.ExecutionSelection.SelectedIssueIds.Count == 0
                ? (string.IsNullOrWhiteSpace(state.ExecutionSelection.Rationale) ? "No execution issues selected." : state.ExecutionSelection.Rationale)
                : $"Issues: {string.Join(", ", state.ExecutionSelection.SelectedIssueIds)}\n\n{state.ExecutionSelection.Rationale}".Trim(),
            "execution-orchestrator",
            sessionId: state.ExecutionSelection.SessionId);
        return state.ExecutionSelection;
    }

    public LoopResult QueueExecutionSelection(WorkspaceState state)
    {
        if (state.ExecutionSelection.SelectedIssueIds.Count == 0)
        {
            return new LoopResult
            {
                State = _issueService.HasBlockingQuestions(state) ? "waiting-for-user" : "idle"
            };
        }

        var selectedIssues = state.ExecutionSelection.SelectedIssueIds
            .Select(issueId => state.Issues.FirstOrDefault(item => item.Id == issueId)
                ?? throw new InvalidOperationException($"Issue #{issueId} was not found."))
            .ToList();
        var queued = QueueIssues(state, selectedIssues);
        ClearExecutionSelection(state);
        return new LoopResult
        {
            State = "queued",
            QueuedRuns = queued
        };
    }

    public QueuedRunInfo QueueSingleIssue(WorkspaceState state, int issueId)
    {
        var issue = state.Issues.FirstOrDefault(i => i.Id == issueId)
            ?? throw new InvalidOperationException($"Issue #{issueId} not found.");
        if (issue.Status != ItemStatus.Open)
            throw new InvalidOperationException($"Issue #{issueId} is not open (current status: {issue.Status}).");

        var queued = QueueIssues(state, [issue]);
        return queued[0];
    }

    public IReadOnlyList<string> GetKnownRoleSlugs(WorkspaceState state) =>
        state.Roles
            .Select(role => role.Slug)
            .OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public RoleModelPolicy GetRoleModelPolicy(WorkspaceState state, string roleSlug) =>
        SeedData.GetPolicy(state, roleSlug);

    public IReadOnlyDictionary<string, string> GetKnownRoleAliases(WorkspaceState state) =>
        _issueService.GetKnownRoleAliases(state);

    public bool TryResolveRoleSlug(WorkspaceState state, string roleSlug, out string resolvedRoleSlug) =>
        _issueService.TryResolveRoleSlug(state, roleSlug, out resolvedRoleSlug);

    public QuestionItem AddQuestion(WorkspaceState state, string text, bool blocking) =>
        _questionService.AddQuestion(state, text, blocking);

    public void AnswerQuestion(WorkspaceState state, int questionId, string answer) =>
        _questionService.AnswerQuestion(state, questionId, answer);

    public IReadOnlyList<QuestionItem> AddQuestions(WorkspaceState state, IEnumerable<ProposedQuestion> questions) =>
        _questionService.AddQuestions(state, questions);

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
            var normalizedRole = _issueService.ResolveRoleSlug(state, proposal.RoleSlug);

            if (state.Phase == WorkflowPhase.Planning
                && string.Equals(normalizedRole, "architect", StringComparison.OrdinalIgnoreCase)
                && state.Issues.Any(i => string.Equals(i.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase) && i.Status != ItemStatus.Done))
            {
                continue;
            }

            var existing = state.Issues.FirstOrDefault(item =>
                string.Equals(item.Title, normalizedTitle, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.RoleSlug, normalizedRole, StringComparison.OrdinalIgnoreCase)
                && item.Status != ItemStatus.Done);
            if (existing is not null)
            {
                continue;
            }

            var issue = _issueService.AddIssue(
                state,
                normalizedTitle,
                proposal.Detail.Trim(),
                normalizedRole,
                proposal.Priority,
                sourceIssue.RoadmapItemId,
                proposal.DependsOnIssueIds,
                proposal.Area,
                "",
                null, null, null, null);
            created.Add(issue);
        }

        return created;
    }

    public LoopResult RunOnce(WorkspaceState state, int maxSubagents = 4)
    {
        var created = PrepareForLoop(state).ToList();
        var readyIssues = _issueService.GetReadyIssues(state, maxSubagents);
        if (readyIssues.Count == 0)
        {
            return new LoopResult
            {
                State = GetLoopStateWhenNoReadyWork(state),
                Created = created
            };
        }

        var queued = new List<QueuedRunInfo>();
        foreach (var issue in readyIssues)
        {
            var model = _budgetService.SelectModelForRole(state, issue.RoleSlug);
            var run = new AgentRun
            {
                Id = state.NextRunId++,
                IssueId = issue.Id,
                RoleSlug = issue.RoleSlug,
                ModelName = model.Name,
                Status = AgentRunStatus.Queued,
                UpdatedAtUtc = _clock.UtcNow
            };
            issue.Status = ItemStatus.InProgress;
            state.AgentRuns.Add(run);
            _budgetService.CommitCredits(state, model);
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
        run.UpdatedAtUtc = _clock.UtcNow;
        switch (outcome.Trim().ToLowerInvariant())
        {
            case "completed":
                run.Status = AgentRunStatus.Completed;
                issue.Status = ItemStatus.Done;
                _issueService.AdvancePipelineAfterCompletion(state, issue);
                break;
            case "failed":
                run.Status = AgentRunStatus.Failed;
                issue.Status = ItemStatus.Open;
                _issueService.UpdatePipelineStatus(state, issue, PipelineStatus.Open, issue.Id);
                break;
            case "blocked":
                run.Status = AgentRunStatus.Blocked;
                issue.Status = ItemStatus.Blocked;
                _issueService.UpdatePipelineStatus(state, issue, PipelineStatus.Blocked, issue.Id);
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
        run.UpdatedAtUtc = _clock.UtcNow;
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId);
        if (issue is not null)
        {
            _issueService.UpdatePipelineStatus(state, issue, PipelineStatus.Running, issue.Id);
        }
    }

    public StatusReport BuildStatusReport(WorkspaceState state)
    {
        _issueService.EnsurePipelineAssignments(state);
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

    private List<QueuedRunInfo> QueueIssues(WorkspaceState state, IReadOnlyList<IssueItem> issues)
    {
        var queued = new List<QueuedRunInfo>();
        foreach (var issue in issues)
        {
            var model = _budgetService.SelectModelForRole(state, issue.RoleSlug);
            var run = new AgentRun
            {
                Id = state.NextRunId++,
                IssueId = issue.Id,
                RoleSlug = issue.RoleSlug,
                ModelName = model.Name,
                Status = AgentRunStatus.Queued,
                UpdatedAtUtc = _clock.UtcNow
            };
            issue.Status = ItemStatus.InProgress;
            state.AgentRuns.Add(run);
            _budgetService.CommitCredits(state, model);
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

        return queued;
    }

    private DecisionRecord RememberDecision(
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
            CreatedAtUtc = _clock.UtcNow
        };
        state.Decisions.Add(record);
        return record;
    }
}
