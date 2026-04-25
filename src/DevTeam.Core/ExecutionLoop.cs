using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevTeam.Core;

public class LoopExecutor(
    DevTeamRuntime runtime,
    WorkspaceStore store,
    IAgentClientFactory? agentClientFactory = null,
    IGitRepository? gitRepository = null,
    ISystemClock? clock = null,
    IFileSystem? fileSystem = null)
{
    private const string NoneText = "(none)";
    private static readonly Regex GitCleanPattern = new(@"\bgit\s+clean\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GitResetHardPattern = new(@"\bgit\s+reset\b[^\r\n;|&]*\s--hard\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GitRestoreForcePattern = new(@"\bgit\s+restore\b[^\r\n;|&]*\s--force\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GitRestoreHeadPattern = new(@"\bgit\s+restore\b[^\r\n;|&]*\b(head|--source=head|--source\s+head)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GitRestoreDotPattern = new(@"\bgit\s+restore\b[^\r\n;|&]*\s\.($|\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GitCheckoutDotPattern = new(@"\bgit\s+checkout\b[^\r\n;|&]*--\s*\.($|\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RemoveDevTeamPattern = new(@"\brm\s+-rf\s+\.devteam\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly DevTeamRuntime _runtime = runtime;
    private readonly WorkspaceStore _store = store;
    private readonly IAgentClientFactory _agentClientFactory = agentClientFactory ?? new DefaultAgentClientFactory();
    private readonly IGitRepository _git = gitRepository ?? new ProcessGitRepository();
    private readonly ISystemClock _clock = clock ?? new SystemClock();
    private readonly IFileSystem _fileSystem = fileSystem ?? new PhysicalFileSystem();

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Main loop coordinates planning, orchestration, and execution state transitions.")]
    [SuppressMessage("Major Code Smell", "S1192", Justification = "Loop outcomes are explicit protocol values shared with prompt contracts.")]
    public async Task<LoopExecutionReport> RunAsync(
        WorkspaceState state,
        LoopExecutionOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var iterations = 0;
        var finalState = "idle";

        for (var iteration = 1; iteration <= options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations = iteration;
            var created = Array.Empty<string>();
            var gitStatusBeforeIteration = _git.TryCaptureStatus(state.RepoRoot);
            var runsToExecute = GetPendingRuns(state, options.MaxSubagents);
            if (runsToExecute.Count == 0)
            {
                if (state.Phase == WorkflowPhase.Execution)
                {
                    created = _runtime.PrepareForLoop(state).ToArray();
                    var orchestration = await OrchestrateExecutionBatchAsync(state, options, log, cancellationToken);
                    finalState = orchestration.State;
                    runsToExecute = orchestration.QueuedRuns.ToList();
                }
                else if (state.Phase == WorkflowPhase.ArchitectPlanning)
                {
                    var result = _runtime.RunOnce(state, options.MaxSubagents);
                    finalState = result.State;
                    created = result.Created.ToArray();
                    runsToExecute = result.QueuedRuns.ToList();

                    if (finalState == "awaiting-architect-approval" && state.Runtime.AutoApproveEnabled)
                    {
                        Log(log, options.Verbosity, "  Auto-approving architect plan (auto-approve enabled).");
                        _runtime.ApproveArchitectPlan(state, "Auto-approved by runtime.");
                        _store.Save(state);
                        continue;
                    }
                }
                else
                {
                    var result = _runtime.RunOnce(state, options.MaxSubagents);
                    finalState = result.State;
                    created = result.Created.ToArray();
                    runsToExecute = result.QueuedRuns.ToList();

                    if (finalState == "awaiting-plan-approval" && state.Runtime.AutoApproveEnabled)
                    {
                        Log(log, options.Verbosity, "  Auto-approving plan (auto-approve enabled).");
                        _runtime.ApprovePlan(state, "Auto-approved by runtime.");
                        _store.Save(state);
                        continue;
                    }
                }
            }
            else
            {
                finalState = "queued";
            }

            Log(log, options.Verbosity, $"Iteration {iteration}: {state.Phase}");
            if (iteration == 1 && state.Phase == WorkflowPhase.Execution && options.MaxSubagents == 1)
            {
                var readyCount = _runtime.GetReadyIssuesPreview(state, 10).Count;
                if (readyCount >= 4)
                {
                    Log(log, options.Verbosity, $"  Hint: {readyCount} issues are ready. Running sequentially (max-subagents=1). Use /max-subagents 3 to run up to 3 in parallel and speed things up.");
                }
            }
            if (created.Length > 0)
            {
                LogDetailed(log, options.Verbosity, $"  Bootstrapped: {string.Join(", ", created)}");
            }

            LogQueuedPipelines(state, runsToExecute, log, options.Verbosity);

            if (finalState != "queued")
            {
                _store.Save(state);
                break;
            }

            var pendingRuns = new List<PendingAgentRun>();
            var canCaptureChangedPathsReliably = state.Runtime.WorktreeMode || runsToExecute.Count <= 1;
            var sharedGitStatusBeforeRun = _git.TryCaptureStatus(state.RepoRoot);
            foreach (var queuedRun in runsToExecute)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // GUARDRAIL: Checkpoint workspace state before this run
                var checkpointPath = CreateWorkspaceCheckpoint(_store.WorkspacePath, queuedRun.RunId);
                LogDetailed(log, options.Verbosity, $"    Checkpoint created: {checkpointPath}");
                
                var session = _runtime.GetOrCreateAgentSession(state, queuedRun.RunId);
                var sessionId = session.SessionId;
                _runtime.StartRun(state, queuedRun.RunId, sessionId);

                // Allocate a git worktree for this run if worktree mode is enabled
                var worktreePath = AllocateWorktree(state, queuedRun);
                var workingDirectory = worktreePath ?? state.RepoRoot;
                var gitStatusBeforeRun = worktreePath is null
                    ? sharedGitStatusBeforeRun
                    : _git.TryCaptureStatus(workingDirectory);

                Log(log, options.Verbosity, $"  Running issue #{queuedRun.IssueId} as {queuedRun.RoleSlug} via {queuedRun.ModelName}");
                var runIssue = state.Issues.FirstOrDefault(i => i.Id == queuedRun.IssueId);
                if (runIssue?.PipelineId is int pid)
                {
                    var pipeline = state.Pipelines.FirstOrDefault(p => p.Id == pid);
                    if (pipeline is not null)
                    {
                        var stageIdx = runIssue.PipelineStageIndex ?? 0;
                        var chain = string.Join(" → ", pipeline.RoleSequence.Select((r, i) => i == stageIdx ? $"[{r}]" : r));
                        Log(log, options.Verbosity, $"    pipeline #{pid} · {chain}");
                    }
                }
                if (worktreePath is not null)
                {
                    LogDetailed(log, options.Verbosity, $"    Worktree: {worktreePath}");
                }
                LogDetailed(log, options.Verbosity, $"    Session {sessionId}, scope {session.ScopeKind}, area {(string.IsNullOrWhiteSpace(queuedRun.Area) ? "none" : queuedRun.Area)}, timeout {options.AgentTimeout.TotalSeconds:0}s");
                pendingRuns.Add(new PendingAgentRun(
                    queuedRun,
                    sessionId,
                    ExecuteRunAsync(state, options, queuedRun, sessionId, overrideWorkingDirectory: worktreePath, log: log, cancellationToken: cancellationToken),
                    _clock.UtcNow,
                    workingDirectory,
                    gitStatusBeforeRun));
            }

            _store.Save(state);

            while (pendingRuns.Count > 0)
            {
                var completed = await AwaitNextCompletionAsync(pendingRuns, options, log, cancellationToken);
                var pendingRun = pendingRuns.First(item => item.Run.RunId == completed.Run.RunId);
                pendingRuns.RemoveAll(item => item.Run.RunId == completed.Run.RunId);
                
                // GUARDRAIL: Validate and restore workspace state if agent deleted .devteam
                var (isValid, restored) = ValidateAndRestoreWorkspaceCheckpoint(_store.WorkspacePath, completed.Run.RunId, log);
                if (!isValid)
                {
                    throw new InvalidOperationException(
                        $"Workspace state (.devteam\\workspace.json) was lost during run #{completed.Run.RunId} and could not be restored from checkpoint. " +
                        $"This indicates an agent may have run a dangerous git command. Execution cannot continue safely.");
                }
                if (restored)
                {
                    Log(log, options.Verbosity, $"    [GUARDRAIL VIOLATION] Agent on run #{completed.Run.RunId} deleted workspace state. Restored from checkpoint.");
                }
                
                DevTeamRuntime.MergeWorkspaceAdditions(state, _store.Load());
                var createdQuestionIds = new List<int>();
                if (completed.Questions.Count > 0)
                {
                    createdQuestionIds = _runtime.AddQuestions(state, completed.Questions)
                        .Select(item => item.Id)
                        .ToList();
                }
                var createdIssueIds = new List<int>();
                if (completed.Issues.Count > 0)
                {
                    createdIssueIds = _runtime.AddGeneratedIssues(state, completed.Run.IssueId, completed.Issues)
                        .Select(item => item.Id)
                        .ToList();
                }
                var changedPaths = canCaptureChangedPathsReliably
                    ? _git.GetPathsChangedSince(pendingRun.WorkingDirectory, pendingRun.GitStatusBeforeRun)
                    : [];
                var completedIssue = state.Issues.First(issue => issue.Id == completed.Run.IssueId);
                var model = state.Models.FirstOrDefault(item => string.Equals(item.Name, completed.Run.ModelName, StringComparison.OrdinalIgnoreCase));
                var usageTelemetry = UsageTelemetryExtractor.Extract(model, completed.Response);
                var decisionCountBeforeCompletion = state.Decisions.Count;
                _runtime.CompleteRun(
                    state,
                    new CompleteRunRequest
                    {
                        RunId = completed.Run.RunId,
                        Outcome = completed.Outcome,
                        Summary = completed.Summary,
                        SkillsUsed = completed.SkillsUsed,
                        ToolsUsed = completed.ToolsUsed,
                        ChangedPaths = changedPaths,
                        CreatedIssueIds = createdIssueIds,
                        CreatedQuestionIds = createdQuestionIds,
                        ResultingIssueStatus = completedIssue.Status,
                        InputTokens = usageTelemetry.InputTokens,
                        OutputTokens = usageTelemetry.OutputTokens,
                        EstimatedCostUsd = usageTelemetry.EstimatedCostUsd
                    });
                var persistedRun = state.AgentRuns.First(run => run.Id == completed.Run.RunId);
                WriteRunArtifact(_store.WorkspacePath, completed.Run, persistedRun, completed.Response, completed.Outcome, completed.Summary);
                if (state.Decisions.Count > decisionCountBeforeCompletion)
                {
                    WriteDecisionArtifact(_store.WorkspacePath, state.Decisions[^1], persistedRun);
                }
                if (!string.IsNullOrWhiteSpace(state.CodebaseContext) && string.Equals(completed.Outcome, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    WriteBrownfieldDeltaLog(_store.WorkspacePath, completedIssue, persistedRun, completed.Approach, completed.Rationale);
                }

                // Merge worktree branch back into the main repo (if worktree mode active for this run)
                FinalizeWorktree(state, completed, log, options.Verbosity);
                var isArchitectRun = !completedIssue.IsPlanningIssue
                    && string.Equals(completedIssue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)
                    && completed.Outcome == "completed";
                if (completedIssue.IsPlanningIssue)
                {
                    WritePlanArtifact(_store.WorkspacePath, state, completed.Summary, "High-level plan");
                }
                else if (isArchitectRun)
                {
                    WritePlanArtifact(_store.WorkspacePath, state, completed.Summary, "Detailed execution plan (architect)");
                }
                _store.Save(state);

                Log(log, options.Verbosity, $"    Outcome: {completed.Outcome}");
                if (completedIssue.PipelineId is int completedPid && completed.Outcome == "completed")
                {
                    var completedPipeline = state.Pipelines.FirstOrDefault(p => p.Id == completedPid);
                    if (completedPipeline is not null && completedPipeline.ActiveIssueId is int nextId && nextId != completedIssue.Id)
                    {
                        var nextIssue = state.Issues.FirstOrDefault(i => i.Id == nextId);
                        if (nextIssue is not null)
                        {
                            Log(log, options.Verbosity, $"    Pipeline handoff → #{nextId} {nextIssue.RoleSlug}: {nextIssue.Title}");
                        }
                    }
                    else if (completedPipeline?.Status == PipelineStatus.Completed)
                    {
                        Log(log, options.Verbosity, $"    Pipeline #{completedPid} completed");
                    }
                }
                if (completedIssue.IsPlanningIssue || isArchitectRun)
                {
                    LogPlanSummary(log, options.Verbosity, completed.Summary);
                    Log(log, options.Verbosity, $"    Plan file: {Path.Combine(_store.WorkspacePath, "plan.md")}");
                }
                if (options.Verbosity == LoopVerbosity.Detailed)
                {
                    Log(log, options.Verbosity, $"    Summary: {completed.Summary}");
                    if (changedPaths.Count > 0)
                    {
                        Log(log, options.Verbosity, $"    Changed files: {string.Join(", ", changedPaths)}");
                    }
                    if (completed.Questions.Count > 0)
                    {
                        Log(log, options.Verbosity, $"    Questions: {completed.Questions.Count}");
                    }
                    if (completed.Issues.Count > 0)
                    {
                        Log(log, options.Verbosity, $"    Proposed issues: {completed.Issues.Count}");
                    }
                    if (!string.IsNullOrWhiteSpace(completed.Response.StdOut))
                    {
                        Log(log, options.Verbosity, $"    Output:\n{completed.Response.StdOut.Trim()}");
                    }
                    if (!string.IsNullOrWhiteSpace(completed.Response.StdErr))
                    {
                        Log(log, options.Verbosity, $"    Error:\n{completed.Response.StdErr.Trim()}");
                    }
                }
                else if (completed.Outcome is "failed" or "blocked")
                {
                    Log(log, options.Verbosity, $"    Detail: {completed.Summary}");
                    if (!string.IsNullOrWhiteSpace(completed.Response.StdErr))
                    {
                        Log(log, options.Verbosity, $"    Error: {completed.Response.StdErr.Trim()}");
                    }
                }
            }

            // After all runs complete, check if blocking questions were created.
            // If so, pause execution and wait for user input.
            var hasBlockingQuestions = state.Questions.Any(q => q.Status == QuestionStatus.Open && q.IsBlocking);
            if (hasBlockingQuestions)
            {
                Log(log, options.Verbosity, "  Blocking question(s) created. Pausing loop to wait for user input.");
                finalState = "waiting-for-user";
                _store.Save(state);
                break;
            }

            LogBudget(state, log, options.Verbosity);
            LogDetailed(log, options.Verbosity, "  Finalizing iteration and staging changed paths...");
            var stagedPaths = _git.StagePathsChangedSince(state.RepoRoot, gitStatusBeforeIteration);
            if (stagedPaths.Count > 0)
            {
                LogDetailed(log, options.Verbosity, $"  Staged {stagedPaths.Count} path(s) for the next orchestrator pass.");
            }
        }

        return new LoopExecutionReport
        {
            IterationsExecuted = iterations,
            FinalState = finalState
        };
    }

    /// <summary>
    /// Execute a single specific issue as a sub-agent call. Used by the spawn_agent MCP tool
    /// so an orchestrator agent can run a child issue within an MCP server session.
    /// </summary>
    public async Task<string> SpawnIssueAsync(
        int issueId,
        string? contextHint,
        string backend,
        TimeSpan agentTimeout,
        CancellationToken cancellationToken)
    {
        var state = _store.Load();
        var queuedRun = _runtime.QueueSingleIssue(state, issueId);
        _store.Save(state);

        var options = new LoopExecutionOptions
        {
            Backend = backend,
            MaxSubagents = 1,
            MaxIterations = 1,
            AgentTimeout = agentTimeout,
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            Verbosity = LoopVerbosity.Normal
        };

        var session = _runtime.GetOrCreateAgentSession(state, queuedRun.RunId);
        _runtime.StartRun(state, queuedRun.RunId, session.SessionId);
        _store.Save(state);

        var gitStatusBeforeRun = _git.TryCaptureStatus(state.RepoRoot);
        AgentExecutionResult result;
        try
        {
            result = await ExecuteRunAsync(state, options, queuedRun, session.SessionId, contextHint: contextHint, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new AgentExecutionResult(
                queuedRun,
                new AgentInvocationResult
                {
                    BackendName = backend,
                    SessionId = session.SessionId,
                    ExitCode = 1,
                    StdErr = "Cancelled by user request."
                },
                "blocked",
                "Cancelled by user request.",
                "",
                "",
                [],
                [],
                [],
                []);
        }

        DevTeamRuntime.MergeWorkspaceAdditions(state, _store.Load());
        var createdQuestionIds = result.Questions.Count > 0
            ? _runtime.AddQuestions(state, result.Questions).Select(item => item.Id).ToList()
            : [];
        var createdIssueIds = result.Issues.Count > 0
            ? _runtime.AddGeneratedIssues(state, result.Run.IssueId, result.Issues).Select(item => item.Id).ToList()
            : [];
        var changedPaths = _git.GetPathsChangedSince(state.RepoRoot, gitStatusBeforeRun);
        var completedIssue = state.Issues.First(issue => issue.Id == result.Run.IssueId);
        var model = state.Models.FirstOrDefault(item => string.Equals(item.Name, result.Run.ModelName, StringComparison.OrdinalIgnoreCase));
        var usageTelemetry = UsageTelemetryExtractor.Extract(model, result.Response);
        var decisionCountBeforeCompletion = state.Decisions.Count;
        _runtime.CompleteRun(
            state,
            new CompleteRunRequest
            {
                RunId = result.Run.RunId,
                Outcome = result.Outcome,
                Summary = result.Summary,
                SkillsUsed = result.SkillsUsed,
                ToolsUsed = result.ToolsUsed,
                ChangedPaths = changedPaths,
                CreatedIssueIds = createdIssueIds,
                CreatedQuestionIds = createdQuestionIds,
                ResultingIssueStatus = completedIssue.Status,
                InputTokens = usageTelemetry.InputTokens,
                OutputTokens = usageTelemetry.OutputTokens,
                EstimatedCostUsd = usageTelemetry.EstimatedCostUsd
            });
        var persistedRun = state.AgentRuns.First(r => r.Id == result.Run.RunId);
        WriteRunArtifact(_store.WorkspacePath, result.Run, persistedRun, result.Response, result.Outcome, result.Summary);
        if (state.Decisions.Count > decisionCountBeforeCompletion)
        {
            WriteDecisionArtifact(_store.WorkspacePath, state.Decisions[^1], persistedRun);
        }
        if (!string.IsNullOrWhiteSpace(state.CodebaseContext) && string.Equals(result.Outcome, "completed", StringComparison.OrdinalIgnoreCase))
        {
            WriteBrownfieldDeltaLog(_store.WorkspacePath, completedIssue, persistedRun, result.Approach, result.Rationale);
        }
        _store.Save(state);

        return $"Issue #{issueId} completed with outcome: {result.Outcome}. Summary: {result.Summary}";
    }

    private async Task<AgentExecutionResult> AwaitNextCompletionAsync(
        IReadOnlyList<PendingAgentRun> pendingRuns,
        LoopExecutionOptions options,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (options.Verbosity == LoopVerbosity.Quiet)
        {
            return await Task.WhenAny(pendingRuns.Select(item => item.Task)).Unwrap();
        }

        while (true)
        {
            var delayTask = Task.Delay(options.HeartbeatInterval, cancellationToken);
            var completionTask = await Task.WhenAny(pendingRuns.Select(item => item.Task).Append(delayTask));
            if (completionTask != delayTask)
            {
                return await ((Task<AgentExecutionResult>)completionTask);
            }

            var running = pendingRuns.Where(item => !item.Task.IsCompleted).ToList();
            if (options.ProgressReporter is null)
            {
                foreach (var item in running)
                {
                    var elapsed = _clock.UtcNow - item.StartedAtUtc;
                    log?.Invoke($"    Still running issue #{item.Run.IssueId} ({elapsed.TotalSeconds:0}s elapsed)...");
                }
            }
            options.ProgressReporter?.Invoke(running
                .Select(item => new RunProgressSnapshot(item.Run.IssueId, item.Run.RoleSlug, item.Run.Title, _clock.UtcNow - item.StartedAtUtc))
                .ToList());
        }
    }

    private static void Log(Action<string>? log, LoopVerbosity verbosity, string message)
    {
        if (verbosity != LoopVerbosity.Quiet)
        {
            log?.Invoke(message);
        }
    }

    private static void LogDetailed(Action<string>? log, LoopVerbosity verbosity, string message)
    {
        if (verbosity == LoopVerbosity.Detailed)
        {
            log?.Invoke(message);
        }
    }

    /// <summary>
    /// Awaits the invocation task while polling for a timeout extension request written by the
    /// workspace MCP server tool <c>request_timeout_extension</c>. When the flag file is found the
    /// deadline is extended once by <see cref="LoopExecutionOptions.TimeoutExtensionAmount"/> and the
    /// file is deleted. When the deadline expires the external <paramref name="timeoutCts"/> is
    /// cancelled, which triggers <see cref="OperationCanceledException"/> on the invocation.
    /// </summary>
    private async Task<AgentInvocationResult> RunWithExtensionMonitoringAsync(
        Task<AgentInvocationResult> invocationTask,
        QueuedRunInfo queuedRun,
        CancellationTokenSource timeoutCts,
        TimeSpan initialTimeout,
        LoopExecutionOptions options,
        Action<string>? log,
        CancellationToken externalCancellation)
    {
        var deadline = _clock.UtcNow + initialTimeout;
        var extensionGranted = false;
        var reqFile = ExtensionReqFilePath(_store.WorkspacePath, queuedRun.IssueId);
        var grantedFile = ExtensionGrantedFilePath(_store.WorkspacePath, queuedRun.IssueId);

        try
        {
            while (true)
            {
                if (invocationTask.IsCompleted)
                {
                    return await invocationTask;
                }

                if (externalCancellation.IsCancellationRequested)
                {
                    return await invocationTask; // Will throw OCE; caller re-throws it.
                }

                var remaining = deadline - _clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    timeoutCts.Cancel();
                    return await invocationTask; // Will throw OCE; caller handles it as timeout.
                }

                var waitFor = remaining < options.HeartbeatInterval ? remaining : options.HeartbeatInterval;
                await Task.WhenAny(invocationTask, Task.Delay(waitFor, externalCancellation));

                if (!extensionGranted && _fileSystem.FileExists(reqFile))
                {
                    try
                    {
                        _fileSystem.DeleteFile(reqFile);
                        var latestState = _store.Load();
                        if (_runtime.TryGrantTimeoutExtension(latestState, queuedRun.IssueId))
                        {
                            _store.Save(latestState);
                            _fileSystem.WriteAllText(grantedFile, "1");
                            deadline += options.TimeoutExtensionAmount;
                            extensionGranted = true;
                            Log(log, options.Verbosity,
                                $"    [{queuedRun.RoleSlug}#{queuedRun.IssueId}] Timeout extension granted (+{(int)options.TimeoutExtensionAmount.TotalMinutes}m). Remaining window extended.");
                        }
                        else
                        {
                            Log(log, options.Verbosity,
                                $"    [{queuedRun.RoleSlug}#{queuedRun.IssueId}] Timeout extension denied (already granted or no active run).");
                        }
                    }
                    catch
                    {
                        // Race condition or I/O error — skip the extension this cycle; it may be retried.
                    }
                }
            }
        }
        finally
        {
            TryDeleteExtensionFile(reqFile);
            TryDeleteExtensionFile(grantedFile);
        }
    }

    private void TryDeleteExtensionFile(string path)
    {
        try
        {
            if (_fileSystem.FileExists(path))
            {
                _fileSystem.DeleteFile(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string ExtensionReqFilePath(string workspacePath, int issueId) =>
        Path.Combine(workspacePath, $"timeout_ext_{issueId}.req");

    private static string ExtensionGrantedFilePath(string workspacePath, int issueId) =>
        Path.Combine(workspacePath, $"timeout_ext_{issueId}.granted");

    /// <summary>
    /// Returns the appropriate timeout for a run. Planning/design roles (architect, orchestrator,
    /// backlog-manager) get <see cref="LoopExecutionOptions.PlanningAgentTimeout"/> because they
    /// explore the codebase and produce multi-issue plans. All others get the standard
    /// <see cref="LoopExecutionOptions.AgentTimeout"/>.
    /// </summary>
    private static TimeSpan ResolveTimeoutForRole(string roleSlug, bool isPlanningIssue, LoopExecutionOptions options)
    {
        if (isPlanningIssue) return options.PlanningAgentTimeout;
        return roleSlug switch
        {
            CoreConstants.Roles.Architect => options.PlanningAgentTimeout,
            "orchestrator" => options.PlanningAgentTimeout,
            "backlog-manager" => options.PlanningAgentTimeout,
            _ => options.AgentTimeout
        };
    }

    private static SessionHooksConfig BuildRunHooks(QueuedRunInfo queuedRun, Action<string>? log, LoopVerbosity verbosity)
    {
        return new SessionHooksConfig
        {
            OnPreToolUse = (toolName, args) =>
            {
                var decision = EvaluatePreToolDecision(toolName, args, log, verbosity, $"{queuedRun.RoleSlug}#{queuedRun.IssueId}");
                if (verbosity == LoopVerbosity.Detailed)
                {
                    log?.Invoke($"    [{queuedRun.RoleSlug}#{queuedRun.IssueId}][tool↓] {toolName}");
                }

                if (decision != PreToolDecision.Allow)
                {
                    return decision;
                }

                return PreToolDecision.Allow;
            },
            OnPostToolUse = (toolName, _, _) =>
            {
                if (verbosity == LoopVerbosity.Detailed)
                {
                    log?.Invoke($"    [{queuedRun.RoleSlug}#{queuedRun.IssueId}][tool↑] {toolName}");
                }
            },
            OnSessionStart = source =>
            {
                if (verbosity == LoopVerbosity.Detailed)
                {
                    log?.Invoke($"    [{queuedRun.RoleSlug}#{queuedRun.IssueId}][session] started ({source})");
                }
            },
            OnErrorOccurred = (context, error) =>
            {
                log?.Invoke($"    [{queuedRun.RoleSlug}#{queuedRun.IssueId}][error] {context}: {error}");
                return ErrorHandlingDecision.Abort;
            }
        };
    }

    private static PreToolDecision EvaluatePreToolDecision(
        string toolName,
        string args,
        Action<string>? log,
        LoopVerbosity verbosity,
        string actorTag)
    {
        if (TryExtractCommandCandidate(toolName, args, out var commandCandidate)
            && TryDetectDangerousGitCommand(commandCandidate, out var reason))
        {
            Log(log, verbosity, $"    [{actorTag}][guardrail] denied tool '{toolName}' because {reason}.");
            return PreToolDecision.Deny;
        }

        return PreToolDecision.Allow;
    }

    private static bool TryExtractCommandCandidate(string toolName, string args, out string commandCandidate)
    {
        commandCandidate = "";
        if (string.IsNullOrWhiteSpace(args))
        {
            return false;
        }

        if (!TryParseToolArgs(args, out var root))
        {
            commandCandidate = args;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(toolName)
            && string.Equals(toolName, "run_in_terminal", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("command", out var runCommand)
            && runCommand.ValueKind == JsonValueKind.String)
        {
            commandCandidate = runCommand.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(commandCandidate);
        }

        if (!string.IsNullOrWhiteSpace(toolName)
            && string.Equals(toolName, "send_to_terminal", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("command", out var sendCommand)
            && sendCommand.ValueKind == JsonValueKind.String)
        {
            commandCandidate = sendCommand.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(commandCandidate);
        }

        if (!string.IsNullOrWhiteSpace(toolName)
            && string.Equals(toolName, "execution_subagent", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("query", out var subagentQuery)
            && subagentQuery.ValueKind == JsonValueKind.String)
        {
            commandCandidate = subagentQuery.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(commandCandidate);
        }

        if (root.TryGetProperty("command", out var commandProperty)
            && commandProperty.ValueKind == JsonValueKind.String)
        {
            commandCandidate = commandProperty.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(commandCandidate);
        }

        if (root.TryGetProperty("query", out var queryProperty)
            && queryProperty.ValueKind == JsonValueKind.String)
        {
            commandCandidate = queryProperty.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(commandCandidate);
        }

        commandCandidate = args;
        return true;
    }

    private static bool TryParseToolArgs(string args, out JsonElement root)
    {
        root = default;
        try
        {
            using var doc = JsonDocument.Parse(args);
            root = doc.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryDetectDangerousGitCommand(string commandText, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        var normalized = commandText.Trim();
        if (!normalized.Contains("git", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("rm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (GitCleanPattern.IsMatch(normalized))
        {
            reason = "git clean can delete .devteam runtime state";
            return true;
        }

        if (GitResetHardPattern.IsMatch(normalized))
        {
            reason = "git reset --hard can wipe in-progress workspace changes";
            return true;
        }

        if (GitRestoreForcePattern.IsMatch(normalized))
        {
            reason = "git restore --force is unsafe for runtime-managed state";
            return true;
        }

        if (GitRestoreHeadPattern.IsMatch(normalized))
        {
            reason = "git restore from HEAD can reset runtime-managed files";
            return true;
        }

        if (GitRestoreDotPattern.IsMatch(normalized))
        {
            reason = "git restore . can reset broad workspace state including .devteam references";
            return true;
        }

        if (GitCheckoutDotPattern.IsMatch(normalized))
        {
            reason = "git checkout -- . can reset broad workspace state";
            return true;
        }

        if (RemoveDevTeamPattern.IsMatch(normalized))
        {
            reason = "rm -rf .devteam deletes runtime state";
            return true;
        }

        return false;
    }

    private static void LogBudget(WorkspaceState state, Action<string>? log, LoopVerbosity verbosity)
    {
        var b = state.Budget;
        Log(log, verbosity, $"  Budget: {b.CreditsCommitted:0.##}/{b.TotalCreditCap:0.##} credits used ({b.PremiumCreditsCommitted:0.##}/{b.PremiumCreditCap:0.##} premium)");
    }

    private static void LogQueuedPipelines(
        WorkspaceState state,
        IReadOnlyList<QueuedRunInfo> runsToExecute,
        Action<string>? log,
        LoopVerbosity verbosity)
    {
        if (verbosity == LoopVerbosity.Quiet || runsToExecute.Count == 0)
        {
            return;
        }

        Log(log, verbosity, "  Planned pipelines:");
        foreach (var run in runsToExecute)
        {
            var issue = state.Issues.First(item => item.Id == run.IssueId);
            var pipelineText = issue.PipelineId is null
                ? "standalone"
                : $"pipeline #{issue.PipelineId} stage {issue.PipelineStageIndex?.ToString() ?? "?"}";
            Log(log, verbosity, $"    - {pipelineText}: {run.RoleSlug} on issue #{run.IssueId} {run.Title}");
        }
    }

    private void WriteRunArtifact(
        string workspacePath,
        QueuedRunInfo queuedRun,
        AgentRun persistedRun,
        AgentInvocationResult response,
        string outcome,
        string summary)
    {
        var runsDir = Path.Combine(workspacePath, "runs");
        _fileSystem.CreateDirectory(runsDir);
        var path = Path.Combine(runsDir, $"run-{queuedRun.RunId:000}.md");
        var usageLines = new List<string>
        {
            $"- Committed credits: {persistedRun.CreditsUsed:0.##}",
            $"- Premium credits: {persistedRun.PremiumCreditsUsed:0.##}"
        };
        if (persistedRun.InputTokens.HasValue || persistedRun.OutputTokens.HasValue)
        {
            var input = persistedRun.InputTokens ?? 0;
            var output = persistedRun.OutputTokens ?? 0;
            usageLines.Add($"- Tokens: {input + output} total ({input} input / {output} output)");
        }
        else
        {
            usageLines.Add("- Tokens: unavailable from backend");
        }

        if (persistedRun.EstimatedCostUsd is double estimatedCostUsd)
        {
            usageLines.Add($"- Estimated USD cost: ${estimatedCostUsd:0.####}");
        }

        var content = $"""
        # Run {queuedRun.RunId}

        - Issue: {queuedRun.IssueId}
        - Role: {queuedRun.RoleSlug}
        - Area: {(string.IsNullOrWhiteSpace(queuedRun.Area) ? NoneText : queuedRun.Area)}
        - Model: {queuedRun.ModelName}
        - Backend: {response.BackendName}
        - Session: {response.SessionId}
        - Outcome: {outcome}
        - Resulting issue status: {(persistedRun.ResultingIssueStatus?.ToString() ?? NoneText)}

        ## Summary

        {summary}

        ## Skills Used

        {(persistedRun.SkillsUsed.Count == 0 ? NoneText : string.Join(Environment.NewLine, persistedRun.SkillsUsed.Select(item => $"- {item}")))}

        ## Tools Used

        {(persistedRun.ToolsUsed.Count == 0 ? NoneText : string.Join(Environment.NewLine, persistedRun.ToolsUsed.Select(item => $"- {item}")))}

        ## Usage

        {string.Join(Environment.NewLine, usageLines)}

        ## Changed Files

        {(persistedRun.ChangedPaths.Count == 0 ? NoneText : string.Join(Environment.NewLine, persistedRun.ChangedPaths.Select(item => $"- {item}")))}

        ## Created Issues

        {(persistedRun.CreatedIssueIds.Count == 0 ? NoneText : string.Join(Environment.NewLine, persistedRun.CreatedIssueIds.Select(item => $"- #{item}")))}

        ## Created Questions

        {(persistedRun.CreatedQuestionIds.Count == 0 ? NoneText : string.Join(Environment.NewLine, persistedRun.CreatedQuestionIds.Select(item => $"- #{item}")))}

        ## Output

        {response.StdOut}

        ## Error

        {response.StdErr}
        """;
        _fileSystem.WriteAllText(path, content);
    }

    private void WriteBrownfieldDeltaLog(
        string workspacePath,
        IssueItem issue,
        AgentRun persistedRun,
        string approach,
        string rationale)
    {
        var path = Path.Combine(workspacePath, "brownfield-delta.md");
        var existing = _fileSystem.FileExists(path)
            ? _fileSystem.ReadAllText(path).TrimEnd()
            : "# Brownfield Change Delta\n\nAppend-only log of how runs handled existing code patterns.\n";
        var changedFiles = persistedRun.ChangedPaths.Count == 0
            ? "- (none)"
            : string.Join(Environment.NewLine, persistedRun.ChangedPaths.Select(item => $"- {item}"));
        var entry = $"""

        ## Run #{persistedRun.Id} — issue #{issue.Id} [{issue.RoleSlug}] {issue.Title}

        - Outcome: {persistedRun.Status}
        - Approach: {(string.IsNullOrWhiteSpace(approach) ? "(not specified)" : approach)}
        - Created: {persistedRun.UpdatedAtUtc:O}

        ### Rationale

        {(string.IsNullOrWhiteSpace(rationale) ? "(not specified)" : rationale.Trim())}

        ### Changed Files

        {changedFiles}
        """;
        _fileSystem.WriteAllText(path, $"{existing}{entry}{Environment.NewLine}");
    }

    private void WriteDecisionArtifact(string workspacePath, DecisionRecord decision, AgentRun? relatedRun = null)
    {
        var decisionsDir = Path.Combine(workspacePath, "decisions");
        _fileSystem.CreateDirectory(decisionsDir);
        var path = Path.Combine(decisionsDir, $"decision-{decision.Id:000}.md");
        var changedFiles = relatedRun?.ChangedPaths.Count > 0
            ? string.Join(Environment.NewLine, relatedRun.ChangedPaths.Select(item => $"- {item}"))
            : NoneText;
        var content = $"""
        # Decision {decision.Id}

        - Source: {decision.Source}
        - Issue: {(decision.IssueId?.ToString() ?? NoneText)}
        - Run: {(decision.RunId?.ToString() ?? NoneText)}
        - Session: {(string.IsNullOrWhiteSpace(decision.SessionId) ? NoneText : decision.SessionId)}
        - Created: {decision.CreatedAtUtc:O}

        ## Title

        {decision.Title}

        ## Detail

        {decision.Detail}

        ## Changed Files

        {changedFiles}
        """;
        _fileSystem.WriteAllText(path, content);
    }

    private void WritePlanArtifact(string workspacePath, WorkspaceState state, string summary, string header = "Current plan")
    {
        var path = Path.Combine(workspacePath, "plan.md");
        var pendingIssues = state.Issues
            .Where(issue => !issue.IsPlanningIssue && issue.Status != ItemStatus.Done)
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Id)
            .ToList();
        var openQuestions = state.Questions
            .Where(question => question.Status == QuestionStatus.Open)
            .OrderBy(question => question.Id)
            .ToList();

        var issueLines = pendingIssues.Count == 0
            ? NoneText
            : string.Join(
                Environment.NewLine,
                pendingIssues.Select(issue =>
                    $"- #{issue.Id} [{issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $" @ {issue.Area}")}] {issue.Title}" +
                    $"{(issue.DependsOnIssueIds.Count == 0 ? "" : $" (depends on {string.Join(", ", issue.DependsOnIssueIds)})")}"));
        var questionLines = openQuestions.Count == 0
            ? NoneText
            : string.Join(
                Environment.NewLine,
                openQuestions.Select(question => $"- #{question.Id} [{(question.IsBlocking ? "blocking" : "non-blocking")}] {question.Text}"));

        var content = $"""
        # {header}

        ## Planning summary

        {summary}

        ## Proposed execution issues

        {issueLines}

        ## Open questions

        {questionLines}

        ## Approval

        Approve this plan with:
        `devteam /approve --workspace {Path.GetFileName(workspacePath)} "Start building."`
        """;
        _fileSystem.WriteAllText(path, content);
    }

    private static void LogPlanSummary(Action<string>? log, LoopVerbosity verbosity, string summary)
    {
        if (verbosity == LoopVerbosity.Quiet)
        {
            return;
        }

        var preview = string.Join(
            Environment.NewLine,
            summary.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(6));
        if (!string.IsNullOrWhiteSpace(preview))
        {
            log?.Invoke("    Plan summary:");
            foreach (var line in preview.Split(Environment.NewLine))
            {
                log?.Invoke($"      {line}");
            }
        }
    }

    private async Task<AgentExecutionResult> ExecuteRunAsync(
        WorkspaceState state,
        LoopExecutionOptions options,
        QueuedRunInfo queuedRun,
        string sessionId,
        string? overrideWorkingDirectory = null,
        string? contextHint = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var client = _agentClientFactory.Create(options.Backend);
        var issue = state.Issues.First(item => item.Id == queuedRun.IssueId);
        var agentTimeout = ResolveTimeoutForRole(queuedRun.RoleSlug, issue.IsPlanningIssue, options);
        var prompt = AgentPromptBuilder.BuildPrompt(state, issue, agentTimeout, contextHint);
        var workingDirectory = overrideWorkingDirectory ?? state.RepoRoot;
        // Use an externally-managed CTS so the timeout is extendable via the MCP request_timeout_extension tool.
        // The failsafe timeout on the request itself is set well beyond agentTimeout + extension so the SDK
        // never kills the session before our own deadline does.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var provider = ProviderSelectionService.ResolveProvider(state, queuedRun.ModelName, options.ProviderName);
            var failsafe = agentTimeout + options.TimeoutExtensionAmount + TimeSpan.FromMinutes(5);
            var invocationTask = client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = queuedRun.ModelName,
                SessionId = sessionId,
                WorkingDirectory = workingDirectory,
                WorkspacePath = _store.WorkspacePath,
                Provider = provider,
                Timeout = failsafe,
                EnableWorkspaceMcp = state.Runtime.WorkspaceMcpEnabled,
                WorkspaceMcpServerName = state.Runtime.WorkspaceMcpServerName,
                ExternalMcpServers = state.McpServers,
                OnToken = options.TokenReporter is null ? null : token => options.TokenReporter($"{queuedRun.RoleSlug}#{queuedRun.IssueId}", token),
                Hooks = BuildRunHooks(queuedRun, log, options.Verbosity),
                CustomAgents = ScoutAgentDefinitions.GetAgentsForRole(queuedRun.RoleSlug)
            }, timeoutCts.Token);
            var response = await RunWithExtensionMonitoringAsync(
                invocationTask, queuedRun, timeoutCts, agentTimeout, options, log, cancellationToken);
            var parsed = AgentPromptBuilder.ParseResponse(response);
            return new AgentExecutionResult(queuedRun, response, parsed.Outcome, parsed.Summary, parsed.Approach, parsed.Rationale, parsed.Issues, parsed.SkillsUsed, parsed.ToolsUsed, parsed.Questions);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var timeoutResponse = new AgentInvocationResult
            {
                BackendName = "timeout",
                SessionId = sessionId,
                ExitCode = 1,
                StdErr = $"Agent timed out after {agentTimeout.TotalSeconds:0} seconds."
            };
            return new AgentExecutionResult(
                queuedRun,
                timeoutResponse,
                "failed",
                timeoutResponse.StdErr,
                "",
                "",
                [],
                [],
                [],
                []);
        }
        catch (Exception ex)
        {
            var response = new AgentInvocationResult
            {
                BackendName = "error",
                SessionId = sessionId,
                ExitCode = 1,
                StdErr = $"Unexpected error: {ex.GetType().Name}: {ex.Message}"
            };
            return new AgentExecutionResult(
                queuedRun,
                response,
                "failed",
                response.StdErr,
                "",
                "",
                [],
                [],
                [],
                []);
        }
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Batch orchestration contains explicit guardrails and fallback behavior.")]
    private async Task<LoopResult> OrchestrateExecutionBatchAsync(
        WorkspaceState state,
        LoopExecutionOptions options,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var candidates = _runtime.GetExecutionCandidatesPreview(state);
        if (candidates.Count == 0)
        {
            return new LoopResult
            {
                State = _runtime.GetLoopStateWhenNoReadyWork(state)
            };
        }

        DevTeamRuntime.ClearExecutionSelection(state);
        var orchestratorSession = _runtime.GetOrCreateExecutionOrchestratorSession(state);
        var prompt = AgentPromptBuilder.BuildOrchestratorPrompt(state, candidates, options.MaxSubagents);
        var client = _agentClientFactory.Create(options.Backend);
        var orchestratorModel = SeedData.GetPolicy(state, "orchestrator").PrimaryModel;
        var provider = ProviderSelectionService.ResolveProvider(state, orchestratorModel, options.ProviderName);
        Log(log, options.Verbosity, $"  Running execution orchestrator via {options.Backend} (session {orchestratorSession.SessionId}, candidates {candidates.Count}, max {options.MaxSubagents})");
        var startedAtUtc = _clock.UtcNow;
        var response = await AwaitInvocationWithHeartbeatAsync(
            client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = orchestratorModel,
                SessionId = orchestratorSession.SessionId,
                WorkingDirectory = state.RepoRoot,
                WorkspacePath = _store.WorkspacePath,
                Provider = provider,
                Timeout = options.PlanningAgentTimeout,
                EnableWorkspaceMcp = state.Runtime.WorkspaceMcpEnabled,
                WorkspaceMcpServerName = state.Runtime.WorkspaceMcpServerName,
                ExternalMcpServers = state.McpServers,
                Hooks = BuildOrchestratorVisibilityHooks(log, options.Verbosity),
                CustomAgents = ScoutAgentDefinitions.GetAgentsForRole("orchestrator")
            }, cancellationToken),
            options,
            log,
            startedAtUtc,
            "execution orchestrator",
            elapsed => new RunProgressSnapshot(null, "orchestrator", "Selecting next execution batch", elapsed),
            cancellationToken);
        var parsed = AgentPromptBuilder.ParseResponse(response);
        Log(log, options.Verbosity, $"  Execution orchestrator outcome: {parsed.Outcome}");
        DevTeamRuntime.MergeWorkspaceAdditions(state, _store.Load());
        if (parsed.Questions.Count > 0)
        {
            _runtime.AddQuestions(state, parsed.Questions);
        }
        if (parsed.Issues.Count > 0)
        {
            var roadmapIssue = state.Issues.FirstOrDefault(item => !item.IsPlanningIssue)
                ?? state.Issues[0];
            _runtime.AddGeneratedIssues(state, roadmapIssue.Id, parsed.Issues);
        }
        var selectedIssueIds = parsed.SelectedIssueIds.Count > 0
            ? parsed.SelectedIssueIds
            : ExtractSelectedIssueIds(response.StdOut);
        if (state.ExecutionSelection.SelectedIssueIds.Count == 0 && selectedIssueIds.Count > 0)
        {
            _runtime.SetExecutionSelection(state, selectedIssueIds, parsed.Summary, orchestratorSession.SessionId, options.MaxSubagents);
        }

        if (selectedIssueIds.Count > 0)
        {
            Log(log, options.Verbosity, $"  Orchestrator selected: {string.Join(", ", selectedIssueIds.Select(id => $"#{id}"))}");
        }

        if (parsed.Outcome is "failed" or "blocked")
        {
            _runtime.RecordDecision(state, "Execution orchestrator did not select a batch", parsed.Summary, "execution-orchestrator", sessionId: orchestratorSession.SessionId);
        }

        _store.Save(state);
        if (state.ExecutionSelection.SelectedIssueIds.Count == 0)
        {
            var fallbackSelection = _runtime.GetReadyIssuesPreview(state, options.MaxSubagents)
                .Select(item => item.Id)
                .ToList();
            if (fallbackSelection.Count > 0)
            {
                Log(log, options.Verbosity, "  Orchestrator did not persist a batch. Falling back to heuristic selection.");
                _runtime.RecordDecision(
                    state,
                    "Execution batch fallback applied",
                    "The orchestrator did not persist a selection, so the runtime used heuristic ready-issue selection.",
                    "execution-orchestrator",
                    sessionId: orchestratorSession.SessionId);
                _runtime.SetExecutionSelection(
                    state,
                    fallbackSelection,
                    string.IsNullOrWhiteSpace(parsed.Summary)
                        ? "Fallback heuristic selection because the orchestrator did not choose a batch."
                        : parsed.Summary,
                    orchestratorSession.SessionId,
                    options.MaxSubagents);
            }
            else
            {
                return new LoopResult
                {
                    State = state.Questions.Any(item => item.Status == QuestionStatus.Open && item.IsBlocking)
                        ? "waiting-for-user"
                        : "idle"
                };
            }
        }

        var queued = _runtime.QueueExecutionSelection(state);
        _store.Save(state);
        return queued;
    }

    private static IReadOnlyList<int> ExtractSelectedIssueIds(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var match = Regex.Match(
            output.Replace("\r\n", "\n", StringComparison.Ordinal),
            @"SELECTED_ISSUES:\s*(?<body>.*?)(?:\n[A-Z_]+:|$)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return [];
        }

        return match.Groups["body"].Value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("-", StringComparison.Ordinal))
            .Select(line => line[1..].Trim())
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .Distinct()
            .ToList();
    }

    private static List<QueuedRunInfo> GetPendingRuns(WorkspaceState state, int maxSubagents)
    {
        var candidateRuns = state.AgentRuns
            .Where(run => run.Status is AgentRunStatus.Running or AgentRunStatus.Queued)
            .OrderByDescending(run => run.Status == AgentRunStatus.Running)
            .ThenBy(run => run.Id)
            .ToList();

        var selected = new List<QueuedRunInfo>();
        var reservedAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in candidateRuns)
        {
            if (selected.Count >= Math.Max(1, maxSubagents))
            {
                break;
            }

            var issue = state.Issues.First(item => item.Id == run.IssueId);
            if (!string.IsNullOrWhiteSpace(issue.Area) && reservedAreas.Contains(issue.Area))
            {
                continue;
            }

            selected.Add(new QueuedRunInfo
            {
                RunId = run.Id,
                IssueId = run.IssueId,
                Title = issue.Title,
                RoleSlug = run.RoleSlug,
                Area = issue.Area,
                ModelName = run.ModelName
            });

            if (!string.IsNullOrWhiteSpace(issue.Area))
            {
                reservedAreas.Add(issue.Area);
            }
        }

        return selected;
    }

    private async Task<T> AwaitInvocationWithHeartbeatAsync<T>(
        Task<T> task,
        LoopExecutionOptions options,
        Action<string>? log,
        DateTimeOffset startedAtUtc,
        string label,
        Func<TimeSpan, RunProgressSnapshot>? snapshotFactory,
        CancellationToken cancellationToken)
    {
        if (options.Verbosity == LoopVerbosity.Quiet)
        {
            return await task;
        }

        while (true)
        {
            var delayTask = Task.Delay(options.HeartbeatInterval, cancellationToken);
            var completionTask = await Task.WhenAny(task, delayTask);
            if (completionTask != delayTask)
            {
                return await task;
            }

            var elapsed = _clock.UtcNow - startedAtUtc;
            if (options.ProgressReporter is null)
            {
                log?.Invoke($"    Still running {label} ({elapsed.TotalSeconds:0}s elapsed)...");
            }
            else if (snapshotFactory is not null)
            {
                options.ProgressReporter.Invoke([snapshotFactory(elapsed)]);
            }
        }
    }

    private static SessionHooksConfig BuildOrchestratorVisibilityHooks(Action<string>? log, LoopVerbosity verbosity)
    {
        return new SessionHooksConfig
        {
            OnPreToolUse = (toolName, args) =>
            {
                var decision = EvaluatePreToolDecision(toolName, args, log, verbosity, "orchestrator");
                if (decision != PreToolDecision.Allow)
                {
                    return decision;
                }

                if (string.Equals(toolName, "spawn_agent", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryExtractIssueId(args, out var issueId))
                    {
                        log?.Invoke($"    [orchestrator][spawn] starting issue #{issueId}");
                    }
                    else
                    {
                        log?.Invoke("    [orchestrator][spawn] starting child issue");
                    }
                }
                else if (string.Equals(toolName, "task", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke("    [orchestrator][inline] running inline subagent task");
                }
                else if (verbosity == LoopVerbosity.Detailed)
                {
                    log?.Invoke($"    [orchestrator][tool↓] {toolName}");
                }

                return PreToolDecision.Allow;
            },
            OnPostToolUse = (toolName, args, result) =>
            {
                if (string.Equals(toolName, "spawn_agent", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryExtractIssueId(args, out var issueId))
                    {
                        log?.Invoke($"    [orchestrator][spawn] completed issue #{issueId}");
                    }
                    else
                    {
                        log?.Invoke("    [orchestrator][spawn] child issue finished");
                    }
                }
                else if (string.Equals(toolName, "task", StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke("    [orchestrator][inline] inline subagent task finished");
                }
                else if (verbosity == LoopVerbosity.Detailed)
                {
                    log?.Invoke($"    [orchestrator][tool↑] {toolName}");
                }
            },
            OnErrorOccurred = (context, error) =>
            {
                log?.Invoke($"    [orchestrator][error] {context}: {error}");
                return ErrorHandlingDecision.Abort;
            }
        };
    }

    private static bool TryExtractIssueId(string args, out int issueId)
    {
        issueId = 0;
        if (string.IsNullOrWhiteSpace(args))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(args);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("issueId", out var issue)
                && issue.ValueKind == JsonValueKind.Number)
            {
                return issue.TryGetInt32(out issueId);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    // ── Workspace guardrail helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a checkpoint of the .devteam workspace state before an agent run.
    /// This protects against accidental deletion by agents running git restore, git clean, or similar.
    /// Returns the checkpoint directory path.
    /// </summary>
    private string CreateWorkspaceCheckpoint(string workspacePath, int runId)
    {
        var checkpointDir = Path.Combine(workspacePath, "checkpoints");
        _fileSystem.CreateDirectory(checkpointDir);
        var checkpointPath = Path.Combine(checkpointDir, $"run-{runId}");
        if (_fileSystem.DirectoryExists(checkpointPath))
        {
            return checkpointPath;
        }

        // Deep copy workspace.json to checkpoint
        if (_fileSystem.FileExists(_store.StatePath))
        {
            _fileSystem.CreateDirectory(checkpointPath);
            var checkpointFile = Path.Combine(checkpointPath, "workspace.json");
            _fileSystem.WriteAllText(checkpointFile, _fileSystem.ReadAllText(_store.StatePath));
        }

        return checkpointPath;
    }

    /// <summary>
    /// Validates that workspace state still exists after an agent run.
    /// If .devteam or workspace.json has been deleted, restores from checkpoint.
    /// Returns (isValid, restoredFromCheckpoint).
    /// </summary>
    private (bool IsValid, bool RestoredFromCheckpoint) ValidateAndRestoreWorkspaceCheckpoint(string workspacePath, int runId, Action<string>? log = null)
    {
        var stateExists = _fileSystem.FileExists(_store.StatePath);
        if (stateExists)
        {
            return (true, false);
        }

        // State is missing — attempt to restore from checkpoint
        var checkpointPath = Path.Combine(workspacePath, "checkpoints", $"run-{runId}");
        var checkpointFile = Path.Combine(checkpointPath, "workspace.json");
        if (_fileSystem.FileExists(checkpointFile))
        {
            LogDetailed(log, LoopVerbosity.Normal, $"    [GUARDRAIL] Workspace state was deleted during run #{runId}. Restoring from checkpoint...");
            _fileSystem.CreateDirectory(workspacePath);
            _fileSystem.WriteAllText(_store.StatePath, _fileSystem.ReadAllText(checkpointFile));
            LogDetailed(log, LoopVerbosity.Normal, $"    [GUARDRAIL] Restored workspace.json from checkpoint.");
            return (true, true);
        }

        return (false, false);
    }

    // ── Worktree helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>WorktreeMode</c> is enabled, creates a git worktree for the run and registers it in
    /// <see cref="WorkspaceState.Worktrees"/>. Returns the worktree path, or <c>null</c> when
    /// worktrees are disabled or the repo is not a git repository.
    /// </summary>
    private string? AllocateWorktree(WorkspaceState state, QueuedRunInfo run)
    {
        if (!state.Runtime.WorktreeMode) return null;
        if (!_git.IsGitRepository(state.RepoRoot)) return null;

        var branchName = $"devteam/issue-{run.IssueId}";
        var worktreePath = Path.Combine(state.RepoRoot, ".devteam", "worktrees", $"issue-{run.IssueId}");

        if (!_git.TryCreateWorktree(state.RepoRoot, worktreePath, branchName))
        {
            return null;
        }

        state.Worktrees.Add(new WorktreeEntry
        {
            IssueId = run.IssueId,
            RunId = run.RunId,
            BranchName = branchName,
            WorktreePath = worktreePath,
            Status = WorktreeStatus.Active
        });
        return worktreePath;
    }

    /// <summary>
    /// After a run completes, merges its worktree branch back into the main repo branch.
    /// On conflict, a conflict-resolution issue is created; on success, the worktree is removed.
    /// No-op when worktree mode is disabled or no worktree exists for the run.
    /// </summary>
    private void FinalizeWorktree(
        WorkspaceState state,
        AgentExecutionResult completed,
        Action<string>? log,
        LoopVerbosity verbosity)
    {
        var worktree = state.Worktrees.FirstOrDefault(w => w.RunId == completed.Run.RunId);
        if (worktree is null) return;

        try
        {
            var commitMsg = $"run #{completed.Run.RunId}: #{completed.Run.IssueId} {completed.Run.Title}";
            var mergeResult = _git.CommitAndMergeWorktree(
                state.RepoRoot, worktree.WorktreePath, worktree.BranchName, commitMsg);

            if (mergeResult.HasConflicts)
            {
                worktree.Status = WorktreeStatus.Conflicted;
                Log(log, verbosity, $"    ⚠ Worktree merge conflict for issue #{worktree.IssueId} — creating conflict resolution issue");
                _runtime.AddGeneratedIssues(state, completed.Run.IssueId,
                [
                    new GeneratedIssueProposal
                    {
                        Title = $"Resolve worktree merge conflicts from issue #{worktree.IssueId}",
                        Detail = $"Issue #{worktree.IssueId} completed but its worktree branch `{worktree.BranchName}` had merge conflicts.\n\nWorktree path: {worktree.WorktreePath}\n\nConflicting files:\n{mergeResult.ConflictSummary}",
                        Area = state.Issues.FirstOrDefault(i => i.Id == worktree.IssueId)?.Area ?? "",
                        RoleSlug = "developer",
                        Priority = 80
                    }
                ]);
            }
            else
            {
                worktree.Status = WorktreeStatus.Merged;
                _git.RemoveWorktree(state.RepoRoot, worktree.WorktreePath, worktree.BranchName);
                state.Worktrees.Remove(worktree);
                Log(log, verbosity, $"    Worktree for issue #{worktree.IssueId} merged and removed.");
            }
        }
        catch (Exception ex)
        {
            Log(log, verbosity, $"    Warning: worktree finalization failed for issue #{worktree.IssueId}: {ex.Message}");
        }
    }
}
