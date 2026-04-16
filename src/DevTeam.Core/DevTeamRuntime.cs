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
        if (!state.Runtime.PipelineRolesCustomized)
        {
            state.Runtime.DefaultPipelineRoles = GetModeDefaultPipelineRoles(mode.Slug);
        }
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

    public void SetDefaultPipelineRoles(WorkspaceState state, IEnumerable<string> roleSlugs)
    {
        var requestedRoles = roleSlugs
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .ToList();
        if (requestedRoles.Count == 0)
        {
            throw new InvalidOperationException("Provide at least one role for the pipeline.");
        }

        var normalizedRoles = new List<string>();
        foreach (var requestedRole in requestedRoles)
        {
            _issueService.TryResolveRoleSlug(state, requestedRole, out var resolvedRole);
            if (!state.Roles.Any(role => string.Equals(role.Slug, resolvedRole, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Unknown role '{requestedRole}'. Valid roles: {string.Join(", ", GetKnownRoleSlugs(state))}");
            }

            if (!normalizedRoles.Contains(resolvedRole, StringComparer.OrdinalIgnoreCase))
            {
                normalizedRoles.Add(resolvedRole);
            }
        }

        state.Runtime.DefaultPipelineRoles = normalizedRoles;
        state.Runtime.PipelineRolesCustomized = true;
        RememberDecision(state, "Updated default pipeline roles", string.Join(" -> ", normalizedRoles), "pipeline");
    }

    public void ResetDefaultPipelineRoles(WorkspaceState state)
    {
        state.Runtime.DefaultPipelineRoles = GetModeDefaultPipelineRoles(state.Runtime.ActiveModeSlug);
        state.Runtime.PipelineRolesCustomized = false;
        RememberDecision(state, "Reset default pipeline roles", string.Join(" -> ", state.Runtime.DefaultPipelineRoles), "pipeline");
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

    public IssueItem EditIssue(WorkspaceState state, IssueEditRequest request)
    {
        var issue = _issueService.EditIssue(state, request);
        RememberDecision(state, $"Edited issue #{issue.Id}", BuildIssueEditDecisionDetail(request, issue), "issue-edit", issueId: issue.Id);
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

    public IReadOnlyList<QueuedRunInfo> BuildRunPreview(WorkspaceState state, int maxSubagents)
    {
        _issueService.EnsurePipelineAssignments(state);
        var readyIssues = _issueService.GetReadyIssues(state, maxSubagents);
        if (readyIssues.Count == 0)
        {
            return [];
        }

        var previewState = new WorkspaceState
        {
            Models = state.Models,
            Roles = state.Roles,
            Budget = new BudgetState
            {
                TotalCreditCap = state.Budget.TotalCreditCap,
                PremiumCreditCap = state.Budget.PremiumCreditCap,
                CreditsCommitted = state.Budget.CreditsCommitted,
                PremiumCreditsCommitted = state.Budget.PremiumCreditsCommitted
            }
        };

        var preview = new List<QueuedRunInfo>();
        foreach (var issue in readyIssues)
        {
            var excludeFamily = GetExcludeFamilyForReview(state, issue);
            var model = _budgetService.SelectModelForRole(previewState, issue.RoleSlug, excludeFamily);
            _budgetService.CommitCredits(previewState, model);
            preview.Add(new QueuedRunInfo
            {
                IssueId = issue.Id,
                Title = issue.Title,
                RoleSlug = issue.RoleSlug,
                Area = issue.Area,
                ModelName = model.Name
            });
        }

        return preview;
    }

    public RunDiffReport BuildRunDiff(WorkspaceState state, int runId, int? compareToRunId = null)
    {
        var primaryRun = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        var primaryIssue = state.Issues.FirstOrDefault(item => item.Id == primaryRun.IssueId);
        var primaryCreatedIssues = state.Issues
            .Where(item => primaryRun.CreatedIssueIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToList();
        var primaryCreatedQuestions = state.Questions
            .Where(item => primaryRun.CreatedQuestionIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToList();

        if (compareToRunId is null)
        {
            return new RunDiffReport
            {
                PrimaryRun = primaryRun,
                PrimaryIssue = primaryIssue,
                PrimaryCreatedIssues = primaryCreatedIssues,
                PrimaryCreatedQuestions = primaryCreatedQuestions,
                PrimaryOnlyChangedPaths = primaryRun.ChangedPaths
            };
        }

        var compareRun = state.AgentRuns.FirstOrDefault(item => item.Id == compareToRunId.Value)
            ?? throw new InvalidOperationException($"Run #{compareToRunId.Value} was not found.");
        var compareIssue = state.Issues.FirstOrDefault(item => item.Id == compareRun.IssueId);
        var compareCreatedIssues = state.Issues
            .Where(item => compareRun.CreatedIssueIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToList();
        var compareCreatedQuestions = state.Questions
            .Where(item => compareRun.CreatedQuestionIds.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToList();

        var primaryChanged = primaryRun.ChangedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var compareChanged = compareRun.ChangedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new RunDiffReport
        {
            PrimaryRun = primaryRun,
            PrimaryIssue = primaryIssue,
            PrimaryCreatedIssues = primaryCreatedIssues,
            PrimaryCreatedQuestions = primaryCreatedQuestions,
            CompareRun = compareRun,
            CompareIssue = compareIssue,
            CompareCreatedIssues = compareCreatedIssues,
            CompareCreatedQuestions = compareCreatedQuestions,
            SharedChangedPaths = primaryChanged.Intersect(compareChanged, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            PrimaryOnlyChangedPaths = primaryChanged.Except(compareChanged, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            CompareOnlyChangedPaths = compareChanged.Except(primaryChanged, StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList()
        };
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
            var excludeFamily = GetExcludeFamilyForReview(state, issue);
            var model = _budgetService.SelectModelForRole(state, issue.RoleSlug, excludeFamily);
            var run = new AgentRun
            {
                Id = state.NextRunId++,
                IssueId = issue.Id,
                RoleSlug = issue.RoleSlug,
                ModelName = model.Name,
                CreditsUsed = model.Cost,
                PremiumCreditsUsed = model.IsPremium ? model.Cost : 0,
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
        IEnumerable<string>? toolsUsed = null,
        IEnumerable<string>? changedPaths = null,
        IEnumerable<int>? createdIssueIds = null,
        IEnumerable<int>? createdQuestionIds = null,
        ItemStatus? resultingIssueStatus = null,
        int? inputTokens = null,
        int? outputTokens = null,
        double? estimatedCostUsd = null)
    {
        var run = state.AgentRuns.FirstOrDefault(item => item.Id == runId)
            ?? throw new InvalidOperationException($"Run #{runId} was not found.");
        var issue = state.Issues.FirstOrDefault(item => item.Id == run.IssueId)
            ?? throw new InvalidOperationException($"Issue #{run.IssueId} was not found.");

        run.Summary = summary.Trim();
        run.ResultingIssueStatus = resultingIssueStatus;
        run.SuperpowersUsed = superpowersUsed?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        run.ToolsUsed = toolsUsed?.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        run.ChangedPaths = changedPaths?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        run.CreatedIssueIds = createdIssueIds?
            .Distinct()
            .OrderBy(item => item)
            .ToList() ?? [];
        run.CreatedQuestionIds = createdQuestionIds?
            .Distinct()
            .OrderBy(item => item)
            .ToList() ?? [];
        run.InputTokens = inputTokens;
        run.OutputTokens = outputTokens;
        run.EstimatedCostUsd = estimatedCostUsd;
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
        var now = _clock.UtcNow;
        var queuedRuns = state.AgentRuns.Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running).ToList();
        var openQuestions = state.Questions.Where(question => question.Status == QuestionStatus.Open).OrderBy(question => question.Id).ToList();
        var openQuestionAges = openQuestions.ToDictionary(
            question => question.Id,
            question => now - NormalizeQuestionCreatedAt(question, now));
        var blockingQuestions = openQuestions
            .Where(question => question.IsBlocking)
            .OrderBy(question => NormalizeQuestionCreatedAt(question, now))
            .ThenBy(question => question.Id)
            .ToList();
        var loopState = queuedRuns.Count > 0 ? "running" : GetLoopStateWhenNoReadyWork(state);
        var isWaitingOnBlockingQuestion = string.Equals(loopState, "waiting-for-user", StringComparison.OrdinalIgnoreCase)
            && blockingQuestions.Count > 0;

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
            LoopState = loopState,
            Budget = state.Budget,
            QueuedRuns = queuedRuns,
            OpenQuestions = openQuestions,
            OpenQuestionAges = openQuestionAges,
            RoleUsage = state.AgentRuns
                .GroupBy(run => run.RoleSlug, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var inputTokens = group
                        .Where(run => run.InputTokens.HasValue)
                        .Sum(run => run.InputTokens ?? 0);
                    var outputTokens = group
                        .Where(run => run.OutputTokens.HasValue)
                        .Sum(run => run.OutputTokens ?? 0);
                    var estimatedUsd = group
                        .Where(run => run.EstimatedCostUsd.HasValue)
                        .Sum(run => run.EstimatedCostUsd ?? 0);
                    return new RoleUsageSummary
                    {
                        RoleSlug = group.Key,
                        RunCount = group.Count(),
                        CompletedRunCount = group.Count(run => run.Status == AgentRunStatus.Completed),
                        CreditsUsed = group.Sum(run => run.CreditsUsed),
                        PremiumCreditsUsed = group.Sum(run => run.PremiumCreditsUsed),
                        InputTokens = group.Any(run => run.InputTokens.HasValue) ? inputTokens : null,
                        OutputTokens = group.Any(run => run.OutputTokens.HasValue) ? outputTokens : null,
                        EstimatedCostUsd = group.Any(run => run.EstimatedCostUsd.HasValue) ? estimatedUsd : null
                    };
                })
                .OrderByDescending(item => item.CreditsUsed)
                .ThenBy(item => item.RoleSlug, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IsWaitingOnBlockingQuestion = isWaitingOnBlockingQuestion,
            OldestBlockingQuestionAge = isWaitingOnBlockingQuestion
                ? openQuestionAges[blockingQuestions[0].Id]
                : null,
            RecentDecisions = state.Decisions
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(5)
                .ToList()
        };
    }

    private static DateTimeOffset NormalizeQuestionCreatedAt(QuestionItem question, DateTimeOffset now)
    {
        if (question.CreatedAtUtc == default || question.CreatedAtUtc > now)
        {
            return now;
        }

        return question.CreatedAtUtc;
    }

    private List<QueuedRunInfo> QueueIssues(WorkspaceState state, IReadOnlyList<IssueItem> issues)
    {
        var queued = new List<QueuedRunInfo>();
        foreach (var issue in issues)
        {
            // Skip issues already handled (e.g. completed inline via spawn_agent during orchestrator session).
            if (issue.Status != ItemStatus.Open)
            {
                continue;
            }

            var excludeFamily = GetExcludeFamilyForReview(state, issue);
            var model = _budgetService.SelectModelForRole(state, issue.RoleSlug, excludeFamily);
            var run = new AgentRun
            {
                Id = state.NextRunId++,
                IssueId = issue.Id,
                RoleSlug = issue.RoleSlug,
                ModelName = model.Name,
                CreditsUsed = model.Cost,
                PremiumCreditsUsed = model.IsPremium ? model.Cost : 0,
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

    /// <summary>
    /// For review-role issues, returns the AI provider family used by the most recent completed
    /// run of any direct dependency issue. Returns null for non-review roles or when no completed
    /// dependency run exists.
    /// </summary>
    private static string? GetExcludeFamilyForReview(WorkspaceState state, IssueItem issue)
    {
        if (!IsReviewRole(issue.RoleSlug)) return null;
        if (issue.DependsOnIssueIds.Count == 0) return null;

        var dependencyModelName = state.AgentRuns
            .Where(run =>
                issue.DependsOnIssueIds.Contains(run.IssueId) &&
                run.Status == AgentRunStatus.Completed &&
                !string.IsNullOrWhiteSpace(run.ModelName))
            .OrderByDescending(run => run.UpdatedAtUtc)
            .Select(run => run.ModelName)
            .FirstOrDefault();

        return dependencyModelName is null ? null : ModelDefinition.InferFamily(dependencyModelName);
    }

    private static bool IsReviewRole(string roleSlug)
    {
        var normalized = roleSlug.Trim().ToLowerInvariant();
        return normalized is "reviewer" or "review" or "security" or "tester";
    }

    private static List<string> GetModeDefaultPipelineRoles(string modeSlug) =>
        modeSlug.Trim().ToLowerInvariant() switch
        {
            "creative-writing" => ["architect", "developer", "reviewer"],
            _ => ["architect", "developer", "tester"]
        };

    private static string BuildIssueEditDecisionDetail(IssueEditRequest request, IssueItem issue)
    {
        var changes = new List<string>();
        if (request.Title is not null)
        {
            changes.Add($"title=\"{issue.Title}\"");
        }

        if (request.Detail is not null)
        {
            changes.Add("detail updated");
        }

        if (request.RoleSlug is not null)
        {
            changes.Add($"role={issue.RoleSlug}");
        }

        if (request.Area is not null || request.ClearArea)
        {
            changes.Add($"area={(string.IsNullOrWhiteSpace(issue.Area) ? "(none)" : issue.Area)}");
        }

        if (request.Priority is not null)
        {
            changes.Add($"priority={issue.Priority}");
        }

        if (request.Status is not null)
        {
            changes.Add($"status={issue.Status}");
        }

        if (request.DependsOnIssueIds is not null || request.ClearDependencies)
        {
            changes.Add(issue.DependsOnIssueIds.Count == 0
                ? "dependencies cleared"
                : $"depends-on={string.Join(", ", issue.DependsOnIssueIds.OrderBy(id => id))}");
        }

        if (!string.IsNullOrWhiteSpace(request.NotesToAppend))
        {
            changes.Add("note appended");
        }

        return changes.Count == 0
            ? "No user-visible fields changed."
            : string.Join("; ", changes);
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
