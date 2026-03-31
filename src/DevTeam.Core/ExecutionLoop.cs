using System.Text.RegularExpressions;

namespace DevTeam.Core;

public sealed class LoopExecutor(
    DevTeamRuntime runtime,
    WorkspaceStore store,
    Func<string, IAgentClient>? agentClientFactory = null)
{
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly WorkspaceStore _store = store;
    private readonly Func<string, IAgentClient> _agentClientFactory = agentClientFactory ?? (backend => AgentClientFactory.Create(backend));

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
            var gitStatusBeforeIteration = GitWorkspace.TryCaptureStatus(state.RepoRoot);
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
            foreach (var queuedRun in runsToExecute)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var session = _runtime.GetOrCreateAgentSession(state, queuedRun.RunId);
                var sessionId = session.SessionId;
                _runtime.StartRun(state, queuedRun.RunId, sessionId);
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
                LogDetailed(log, options.Verbosity, $"    Session {sessionId}, scope {session.ScopeKind}, area {(string.IsNullOrWhiteSpace(queuedRun.Area) ? "none" : queuedRun.Area)}, timeout {options.AgentTimeout.TotalSeconds:0}s");
                pendingRuns.Add(new PendingAgentRun(
                    queuedRun,
                    sessionId,
                    ExecuteRunAsync(state, options, queuedRun, sessionId, cancellationToken),
                    DateTimeOffset.UtcNow));
            }

            _store.Save(state);

            while (pendingRuns.Count > 0)
            {
                var completed = await AwaitNextCompletionAsync(pendingRuns, options, log, cancellationToken);
                pendingRuns.RemoveAll(item => item.Run.RunId == completed.Run.RunId);
                _runtime.MergeWorkspaceAdditions(state, _store.Load());
                if (completed.Questions.Count > 0)
                {
                    _runtime.AddQuestions(state, completed.Questions);
                }
                if (completed.Issues.Count > 0)
                {
                    _runtime.AddGeneratedIssues(state, completed.Run.IssueId, completed.Issues);
                }
                _runtime.CompleteRun(state, completed.Run.RunId, completed.Outcome, completed.Summary, completed.SuperpowersUsed, completed.ToolsUsed);
                var decision = state.Decisions.Last();
                var persistedRun = state.AgentRuns.First(run => run.Id == completed.Run.RunId);
                WriteRunArtifact(_store.WorkspacePath, completed.Run, persistedRun, completed.Response, completed.Outcome, completed.Summary);
                WriteDecisionArtifact(_store.WorkspacePath, decision);
                var completedIssue = state.Issues.First(issue => issue.Id == completed.Run.IssueId);
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

            LogBudget(state, log, options.Verbosity);
            LogDetailed(log, options.Verbosity, "  Finalizing iteration and staging changed paths...");
            var stagedPaths = GitWorkspace.StagePathsChangedSince(state.RepoRoot, gitStatusBeforeIteration);
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

    private static async Task<AgentExecutionResult> AwaitNextCompletionAsync(
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
                    var elapsed = DateTimeOffset.UtcNow - item.StartedAtUtc;
                    log?.Invoke($"    Still running issue #{item.Run.IssueId} ({elapsed.TotalSeconds:0}s elapsed)...");
                }
            }
            options.ProgressReporter?.Invoke(running
                .Select(item => new RunProgressSnapshot(item.Run.IssueId, item.Run.RoleSlug, item.Run.Title, DateTimeOffset.UtcNow - item.StartedAtUtc))
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

    private static void WriteRunArtifact(
        string workspacePath,
        QueuedRunInfo queuedRun,
        AgentRun persistedRun,
        AgentInvocationResult response,
        string outcome,
        string summary)
    {
        var runsDir = Path.Combine(workspacePath, "runs");
        Directory.CreateDirectory(runsDir);
        var path = Path.Combine(runsDir, $"run-{queuedRun.RunId:000}.md");
        var content = $"""
        # Run {queuedRun.RunId}

        - Issue: {queuedRun.IssueId}
        - Role: {queuedRun.RoleSlug}
        - Area: {(string.IsNullOrWhiteSpace(queuedRun.Area) ? "(none)" : queuedRun.Area)}
        - Model: {queuedRun.ModelName}
        - Backend: {response.BackendName}
        - Session: {response.SessionId}
        - Outcome: {outcome}

        ## Summary

        {summary}

        ## Superpowers Used

        {(persistedRun.SuperpowersUsed.Count == 0 ? "(none)" : string.Join(Environment.NewLine, persistedRun.SuperpowersUsed.Select(item => $"- {item}")))}

        ## Tools Used

        {(persistedRun.ToolsUsed.Count == 0 ? "(none)" : string.Join(Environment.NewLine, persistedRun.ToolsUsed.Select(item => $"- {item}")))}

        ## Output

        {response.StdOut}

        ## Error

        {response.StdErr}
        """;
        File.WriteAllText(path, content);
    }

    private static void WriteDecisionArtifact(string workspacePath, DecisionRecord decision)
    {
        var decisionsDir = Path.Combine(workspacePath, "decisions");
        Directory.CreateDirectory(decisionsDir);
        var path = Path.Combine(decisionsDir, $"decision-{decision.Id:000}.md");
        var content = $"""
        # Decision {decision.Id}

        - Source: {decision.Source}
        - Issue: {(decision.IssueId?.ToString() ?? "(none)")}
        - Run: {(decision.RunId?.ToString() ?? "(none)")}
        - Session: {(string.IsNullOrWhiteSpace(decision.SessionId) ? "(none)" : decision.SessionId)}
        - Created: {decision.CreatedAtUtc:O}

        ## Title

        {decision.Title}

        ## Detail

        {decision.Detail}
        """;
        File.WriteAllText(path, content);
    }

    private static void WritePlanArtifact(string workspacePath, WorkspaceState state, string summary, string header = "Current plan")
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
            ? "(none)"
            : string.Join(
                Environment.NewLine,
                pendingIssues.Select(issue =>
                    $"- #{issue.Id} [{issue.RoleSlug}{(string.IsNullOrWhiteSpace(issue.Area) ? "" : $" @ {issue.Area}")}] {issue.Title}" +
                    $"{(issue.DependsOnIssueIds.Count == 0 ? "" : $" (depends on {string.Join(", ", issue.DependsOnIssueIds)})")}"));
        var questionLines = openQuestions.Count == 0
            ? "(none)"
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
        File.WriteAllText(path, content);
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
        CancellationToken cancellationToken)
    {
        var client = _agentClientFactory(options.Backend);
        var issue = state.Issues.First(item => item.Id == queuedRun.IssueId);
        var prompt = AgentPromptBuilder.BuildPrompt(state, issue);
        try
        {
            var response = await client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = queuedRun.ModelName,
                SessionId = sessionId,
                WorkingDirectory = state.RepoRoot,
                WorkspacePath = _store.WorkspacePath,
                Timeout = options.AgentTimeout,
                EnableWorkspaceMcp = state.Runtime.WorkspaceMcpEnabled,
                WorkspaceMcpServerName = state.Runtime.WorkspaceMcpServerName,
                ExternalMcpServers = state.McpServers
            }, cancellationToken);
            var parsed = AgentPromptBuilder.ParseResponse(response);
            return new AgentExecutionResult(queuedRun, response, parsed.Outcome, parsed.Summary, parsed.Issues, parsed.SuperpowersUsed, parsed.ToolsUsed, parsed.Questions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var response = new AgentInvocationResult
            {
                BackendName = "timeout",
                SessionId = sessionId,
                ExitCode = 1,
                StdErr = $"Agent timed out after {options.AgentTimeout.TotalSeconds:0} seconds."
            };
            return new AgentExecutionResult(
                queuedRun,
                response,
                "failed",
                response.StdErr,
                [],
                [],
                [],
                []);
        }
    }

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

        _runtime.ClearExecutionSelection(state);
        var orchestratorSession = _runtime.GetOrCreateExecutionOrchestratorSession(state);
        var prompt = AgentPromptBuilder.BuildOrchestratorPrompt(state, candidates, options.MaxSubagents);
        var client = _agentClientFactory(options.Backend);
        Log(log, options.Verbosity, $"  Running execution orchestrator via {options.Backend} (session {orchestratorSession.SessionId}, candidates {candidates.Count}, max {options.MaxSubagents})");
        var startedAtUtc = DateTimeOffset.UtcNow;
        var response = await AwaitInvocationWithHeartbeatAsync(
            client.InvokeAsync(new AgentInvocationRequest
            {
                Prompt = prompt,
                Model = SeedData.GetPolicy(state, "orchestrator").PrimaryModel,
                SessionId = orchestratorSession.SessionId,
                WorkingDirectory = state.RepoRoot,
                WorkspacePath = _store.WorkspacePath,
                Timeout = options.AgentTimeout,
                EnableWorkspaceMcp = state.Runtime.WorkspaceMcpEnabled,
                WorkspaceMcpServerName = state.Runtime.WorkspaceMcpServerName,
                ExternalMcpServers = state.McpServers
            }, cancellationToken),
            options,
            log,
            startedAtUtc,
            "execution orchestrator",
            elapsed => new RunProgressSnapshot(null, "orchestrator", "Selecting next execution batch", elapsed),
            cancellationToken);
        var parsed = AgentPromptBuilder.ParseResponse(response);
        Log(log, options.Verbosity, $"  Execution orchestrator outcome: {parsed.Outcome}");
        _runtime.MergeWorkspaceAdditions(state, _store.Load());
        if (parsed.Questions.Count > 0)
        {
            _runtime.AddQuestions(state, parsed.Questions);
        }
        if (parsed.Issues.Count > 0)
        {
            var roadmapIssue = state.Issues.FirstOrDefault(item => !item.IsPlanningIssue)
                ?? state.Issues.First();
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

    private static async Task<T> AwaitInvocationWithHeartbeatAsync<T>(
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

            var elapsed = DateTimeOffset.UtcNow - startedAtUtc;
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
}

