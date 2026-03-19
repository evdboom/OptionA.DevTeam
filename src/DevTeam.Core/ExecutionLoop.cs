using System.Text.RegularExpressions;

namespace DevTeam.Core;

public sealed class LoopExecutionOptions
{
    public int MaxIterations { get; set; } = 10;
    public int MaxSubagents { get; set; } = 1;
    public string Backend { get; set; } = "sdk";
    public TimeSpan AgentTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public LoopVerbosity Verbosity { get; set; } = LoopVerbosity.Normal;
}

public enum LoopVerbosity
{
    Quiet,
    Normal,
    Detailed
}

public sealed class LoopExecutionReport
{
    public int IterationsExecuted { get; init; }
    public string FinalState { get; init; } = "idle";
}

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
            var runsToExecute = GetPendingRuns(state, options.MaxSubagents);
            if (runsToExecute.Count == 0)
            {
                var result = _runtime.RunOnce(state, options.MaxSubagents);
                finalState = result.State;
                created = result.Created.ToArray();
                runsToExecute = result.QueuedRuns.ToList();
            }
            else
            {
                finalState = "queued";
            }

            Log(log, options.Verbosity, $"Iteration {iteration}: state={finalState}");
            if (created.Length > 0)
            {
                Log(log, options.Verbosity, $"  Bootstrapped: {string.Join(", ", created)}");
            }

            Log(log, options.Verbosity, $"  Phase: {state.Phase}");

            if (finalState != "queued")
            {
                _store.Save(state);
                break;
            }

            var pendingRuns = new List<(QueuedRunInfo Run, string SessionId, Task<AgentExecutionResult> Task)>();
            foreach (var queuedRun in runsToExecute)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existingRun = state.AgentRuns.First(item => item.Id == queuedRun.RunId);
                var sessionId = string.IsNullOrWhiteSpace(existingRun.SessionId)
                    ? $"{queuedRun.RoleSlug}-run-{queuedRun.RunId:000}"
                    : existingRun.SessionId;
                _runtime.StartRun(state, queuedRun.RunId, sessionId);
                Log(log, options.Verbosity, $"  Running issue #{queuedRun.IssueId} as {queuedRun.RoleSlug} via {queuedRun.ModelName} (session {sessionId}, area {(string.IsNullOrWhiteSpace(queuedRun.Area) ? "none" : queuedRun.Area)}, timeout {options.AgentTimeout.TotalSeconds:0}s)");
                pendingRuns.Add((queuedRun, sessionId, ExecuteRunAsync(state, options, queuedRun, sessionId, cancellationToken)));
            }

            _store.Save(state);

            foreach (var item in pendingRuns)
            {
                var completed = await AwaitWithHeartbeat(item.Run, item.Task, options, log, cancellationToken);
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
                if (state.Issues.First(issue => issue.Id == completed.Run.IssueId).IsPlanningIssue)
                {
                    WritePlanArtifact(_store.WorkspacePath, state, completed.Summary);
                }
                _store.Save(state);

                Log(log, options.Verbosity, $"    Outcome: {completed.Outcome}");
                if (state.Issues.First(issue => issue.Id == completed.Run.IssueId).IsPlanningIssue)
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
            }
        }

        return new LoopExecutionReport
        {
            IterationsExecuted = iterations,
            FinalState = finalState
        };
    }

    private static async Task<AgentExecutionResult> AwaitWithHeartbeat(
        QueuedRunInfo run,
        Task<AgentExecutionResult> task,
        LoopExecutionOptions options,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (options.Verbosity == LoopVerbosity.Quiet)
        {
            return await task;
        }

        var startedAt = DateTimeOffset.UtcNow;
        while (!task.IsCompleted)
        {
            var completedTask = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            if (completedTask == task)
            {
                break;
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            log?.Invoke($"    Still running issue #{run.IssueId} ({elapsed.TotalSeconds:0}s elapsed)...");
        }

        return await task;
    }

    private static void Log(Action<string>? log, LoopVerbosity verbosity, string message)
    {
        if (verbosity != LoopVerbosity.Quiet)
        {
            log?.Invoke(message);
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

    private static void WritePlanArtifact(string workspacePath, WorkspaceState state, string summary)
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
        # Current plan

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
                Timeout = options.AgentTimeout
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
}

internal static class AgentPromptBuilder
{
    private static readonly Dictionary<string, string[]> RoleSuperpowerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orchestrator"] = ["brainstorm", "plan"],
        ["architect"] = ["brainstorm", "plan"],
        ["developer"] = ["plan", "tdd", "verify"],
        ["backend-developer"] = ["plan", "tdd", "verify"],
        ["frontend-developer"] = ["plan", "tdd", "verify"],
        ["fullstack-developer"] = ["plan", "tdd", "verify"],
        ["tester"] = ["tdd", "debug", "verify"],
        ["reviewer"] = ["review", "verify"],
        ["ux"] = ["verify"],
        ["user"] = ["verify"]
    };

    public static string BuildPrompt(WorkspaceState state, IssueItem issue)
    {
        var role = state.Roles.FirstOrDefault(item => item.Slug == issue.RoleSlug);
        var roleBody = role?.Body ?? $"# Role: {issue.RoleSlug}";
        var roleTools = role?.RequiredTools ?? [];
        var superpowers = ResolveSuperpowers(state, issue.RoleSlug);
        var questionBlock = BuildQuestionBlock(state);
        var tools = roleTools
            .Concat(superpowers.SelectMany(item => item.RequiredTools))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var superpowerBlock = string.Join(
            "\n\n---\n\n",
            superpowers.Select(item => item.Body.Trim()));
        var availableRoles = string.Join(", ", state.Roles.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        var availableSuperpowers = superpowers.Count == 0
            ? "(none)"
            : string.Join(", ", superpowers.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

        return $"""
        You are working inside the DevTeam runtime.

        Current phase:
        {state.Phase}

        Active goal:
        {state.ActiveGoal?.GoalText ?? "(no active goal)"}

        Current issue:
        - Id: {issue.Id}
        - Title: {issue.Title}
        - Detail: {issue.Detail}
        - Role: {issue.RoleSlug}
        - Area: {(string.IsNullOrWhiteSpace(issue.Area) ? "(none)" : issue.Area)}

        Role instructions:
        {roleBody.Trim()}

        Relevant superpowers:
        {superpowerBlock}

        Relevant superpower slugs:
        {availableSuperpowers}

        Declared tool expectations:
        {(tools.Count == 0 ? "(none declared)" : string.Join(", ", tools))}

        Open questions:
        {questionBlock}

        Recent decisions:
        {BuildDecisionBlock(state)}

        Valid role slugs for ISSUES:
        {availableRoles}

        Task:
        Work on the current issue using the available tools and role guidance. Keep the scope narrow. If you discover follow-on work, a blocker, a prerequisite, or a natural decomposition that should not be absorbed into the current issue, add it under ISSUES instead of expanding scope. This includes new issues for the same role when the current issue should stay small. If you are blocked by missing information, say so clearly. Do not try to ask the user interactively. Instead, put every needed user question in the QUESTIONS section.

        Reply in exactly this shape:
        OUTCOME: completed|blocked|failed
        SUMMARY:
        <short summary>
        ISSUES:
        - role=<role>; area=<shared-work-area-or-none>; priority=<1-100>; depends=<comma-separated existing issue ids or none>; title=<title>; detail=<detail>
        If no issues should be created, write `(none)` under ISSUES.
        SUPERPOWERS_USED:
        - <superpower slug>
        List only the superpowers you actually used from the provided guidance. If none, write `(none)`.
        TOOLS_USED:
        - <tool name or command>
        List the concrete tools or commands you actually used. If none, write `(none)`.
        QUESTIONS:
        - [blocking] <question text>
        - [non-blocking] <question text>
        If you do not need user input, write `(none)` under QUESTIONS.
        """;
    }

    public static ParsedAgentResponse ParseResponse(AgentInvocationResult response)
    {
        if (!response.Success)
        {
            var error = string.IsNullOrWhiteSpace(response.StdErr) ? "Agent invocation failed." : response.StdErr.Trim();
            return new ParsedAgentResponse("failed", error, [], [], [], []);
        }

        var text = NormalizeStructuredResponse(response.StdOut).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedAgentResponse("completed", "Agent returned no summary.", [], [], [], []);
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var outcomeLine = lines.FirstOrDefault(line => line.StartsWith("OUTCOME:", StringComparison.OrdinalIgnoreCase));
        var summaryIndex = Array.FindIndex(lines, line => line.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase));
        var issuesIndex = Array.FindIndex(lines, line => line.StartsWith("ISSUES:", StringComparison.OrdinalIgnoreCase));
        var superpowersIndex = Array.FindIndex(lines, line => line.StartsWith("SUPERPOWERS_USED:", StringComparison.OrdinalIgnoreCase));
        var toolsIndex = Array.FindIndex(lines, line => line.StartsWith("TOOLS_USED:", StringComparison.OrdinalIgnoreCase));
        var questionsIndex = Array.FindIndex(lines, line => line.StartsWith("QUESTIONS:", StringComparison.OrdinalIgnoreCase));

        var outcome = "completed";
        if (!string.IsNullOrWhiteSpace(outcomeLine))
        {
            outcome = outcomeLine["OUTCOME:".Length..].Trim().ToLowerInvariant();
            if (outcome is not ("completed" or "blocked" or "failed"))
            {
                outcome = "completed";
            }
        }

        string summary;
        if (summaryIndex >= 0)
        {
            var summaryEndIndexCandidates = new[] { issuesIndex, superpowersIndex, toolsIndex, questionsIndex }
                .Where(index => index >= 0 && index > summaryIndex)
                .ToList();
            var summaryEndIndex = summaryEndIndexCandidates.Count > 0 ? summaryEndIndexCandidates.Min() : lines.Length;
            var tail = lines.Skip(summaryIndex + 1).Take(summaryEndIndex - summaryIndex - 1).ToList();
            if (tail.Count == 0)
            {
                summary = "No summary provided.";
            }
            else
            {
                summary = string.Join('\n', tail).Trim();
            }
        }
        else
        {
            summary = text;
        }

        var issues = ParseIssues(lines, issuesIndex, questionsIndex);
        var superpowersUsed = ParseSimpleList(lines, superpowersIndex, toolsIndex, questionsIndex);
        var toolsUsed = ParseSimpleList(lines, toolsIndex, questionsIndex);
        var questions = ParseQuestions(lines, questionsIndex);
        return new ParsedAgentResponse(
            outcome,
            string.IsNullOrWhiteSpace(summary) ? "No summary provided." : summary,
            issues,
            superpowersUsed,
            toolsUsed,
            questions);
    }

    private static string NormalizeStructuredResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var header in new[] { "OUTCOME:", "SUMMARY:", "ISSUES:", "SUPERPOWERS_USED:", "TOOLS_USED:", "QUESTIONS:" })
        {
            normalized = Regex.Replace(
                normalized,
                $"(?<!^)(?<!\\n)({Regex.Escape(header)})",
                "\n$1",
                RegexOptions.CultureInvariant);
        }

        return normalized;
    }

    private static List<SuperpowerDefinition> ResolveSuperpowers(WorkspaceState state, string roleSlug)
    {
        if (!RoleSuperpowerMap.TryGetValue(roleSlug, out var slugs))
        {
            return [];
        }

        return state.Superpowers
            .Where(item => slugs.Contains(item.Slug, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildQuestionBlock(WorkspaceState state)
    {
        var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
        if (openQuestions.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            "\n",
            openQuestions.Select(item => $"- #{item.Id} [{(item.IsBlocking ? "blocking" : "non-blocking")}] {item.Text}"));
    }

    private static string BuildDecisionBlock(WorkspaceState state)
    {
        var recentDecisions = state.Decisions
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(5)
            .ToList();
        if (recentDecisions.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            "\n",
            recentDecisions.Select(item => $"- #{item.Id} [{item.Source}] {item.Title}: {item.Detail}"));
    }

    private static IReadOnlyList<ProposedQuestion> ParseQuestions(string[] lines, int questionsIndex)
    {
        if (questionsIndex < 0)
        {
            return [];
        }

        var result = new List<ProposedQuestion>();
        foreach (var rawLine in lines.Skip(questionsIndex + 1))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "(none)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var body = line[1..].Trim();
            if (body.StartsWith("[blocking]", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ProposedQuestion
                {
                    IsBlocking = true,
                    Text = body["[blocking]".Length..].Trim()
                });
            }
            else if (body.StartsWith("[non-blocking]", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new ProposedQuestion
                {
                    IsBlocking = false,
                    Text = body["[non-blocking]".Length..].Trim()
                });
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ParseSimpleList(string[] lines, int sectionIndex, params int[] otherSectionIndexes)
    {
        if (sectionIndex < 0)
        {
            return [];
        }

        var endIndex = otherSectionIndexes
            .Where(index => index >= 0 && index > sectionIndex)
            .DefaultIfEmpty(lines.Length)
            .Min();
        var result = new List<string>();
        foreach (var rawLine in lines.Skip(sectionIndex + 1).Take(endIndex - sectionIndex - 1))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "(none)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                result.Add(line[1..].Trim());
            }
        }

        return result
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<GeneratedIssueProposal> ParseIssues(string[] lines, int issuesIndex, int questionsIndex)
    {
        if (issuesIndex < 0)
        {
            return [];
        }

        var sectionIndexes = new[]
        {
            Array.FindIndex(lines, line => line.StartsWith("SUPERPOWERS_USED:", StringComparison.OrdinalIgnoreCase)),
            Array.FindIndex(lines, line => line.StartsWith("TOOLS_USED:", StringComparison.OrdinalIgnoreCase)),
            questionsIndex
        };
        var endIndex = sectionIndexes
            .Where(index => index >= 0 && index > issuesIndex)
            .DefaultIfEmpty(lines.Length)
            .Min();
        var result = new List<GeneratedIssueProposal>();
        foreach (var rawLine in lines.Skip(issuesIndex + 1).Take(endIndex - issuesIndex - 1))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || string.Equals(line, "(none)", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line[1..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                var separatorIndex = field.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                values[field[..separatorIndex].Trim()] = field[(separatorIndex + 1)..].Trim();
            }

            if (!values.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            values.TryGetValue("role", out var role);
            values.TryGetValue("area", out var area);
            values.TryGetValue("detail", out var detail);
            var priority = values.TryGetValue("priority", out var priorityText) && int.TryParse(priorityText, out var parsedPriority)
                ? parsedPriority
                : 50;
            var depends = values.TryGetValue("depends", out var dependsText)
                ? dependsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
                    .Where(value => value > 0)
                    .ToList()
                : [];

            result.Add(new GeneratedIssueProposal
            {
                Title = title.Trim(),
                Detail = detail?.Trim() ?? "",
                RoleSlug = string.IsNullOrWhiteSpace(role) ? "developer" : role.Trim(),
                Area = string.Equals(area, "none", StringComparison.OrdinalIgnoreCase) ? "" : area?.Trim() ?? "",
                Priority = priority,
                DependsOnIssueIds = depends
            });
        }

        return result;
    }
}

internal sealed record AgentExecutionResult(
    QueuedRunInfo Run,
    AgentInvocationResult Response,
    string Outcome,
    string Summary,
    IReadOnlyList<GeneratedIssueProposal> Issues,
    IReadOnlyList<string> SuperpowersUsed,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ProposedQuestion> Questions);

internal sealed record ParsedAgentResponse(
    string Outcome,
    string Summary,
    IReadOnlyList<GeneratedIssueProposal> Issues,
    IReadOnlyList<string> SuperpowersUsed,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ProposedQuestion> Questions);
