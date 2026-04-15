using System.Text.RegularExpressions;

namespace DevTeam.Core;

public static class AgentPromptBuilder
{
    private static readonly Dictionary<string, string[]> RoleSuperpowerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orchestrator"] = ["brainstorm", "plan"],
        ["planner"] = ["brainstorm", "plan"],
        ["architect"] = ["brainstorm", "plan"],
        ["developer"] = ["plan", "tdd", "verify"],
        ["backend-developer"] = ["plan", "tdd", "verify"],
        ["frontend-developer"] = ["plan", "tdd", "verify"],
        ["fullstack-developer"] = ["plan", "tdd", "verify"],
        ["tester"] = ["tdd", "debug", "verify"],
        ["reviewer"] = ["review", "verify"],
        ["ux"] = ["verify"],
        ["user"] = ["verify"],
        ["game-designer"] = ["brainstorm", "review", "verify"],
        ["conflict-resolver"] = ["resolve-conflict"]
    };

    private static readonly HashSet<string> DesignOnlyRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "planner", "orchestrator", "architect", "navigator", "analyst", "security", "reviewer"
    };

    private static string BuildFileBoundaryBlock(string roleSlug)
    {
        if (!DesignOnlyRoles.Contains(roleSlug))
        {
            return "";
        }
        return """

        FILE BOUNDARY (enforced by runtime):
        You are a design-only role. Do NOT create, edit, or delete source code files.
        Do NOT run commands that create files or directories (npm init, dotnet new, mkdir, touch, etc.).
        Describe structures, patterns, and decisions in your SUMMARY. Create ISSUES for implementation work.
        Violations will be flagged as role-boundary errors.
        """;
    }

    public static string BuildPrompt(WorkspaceState state, IssueItem issue)
    {
        var role = state.Roles.FirstOrDefault(item => item.Slug == issue.RoleSlug);
        var activeMode = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, state.Runtime.ActiveModeSlug, StringComparison.OrdinalIgnoreCase))
            ?? state.Modes.FirstOrDefault();
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

        Active mode:
        {(activeMode is null ? state.Runtime.ActiveModeSlug : $"{activeMode.Slug} ({activeMode.Name})")}

        Mode guardrails:
        {(activeMode?.Body.Trim() ?? "(none)")}
        {BuildCodebaseContextBlock(state, issue.RoleSlug)}
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

        Workspace MCP:
        {(state.Runtime.WorkspaceMcpEnabled ? "A local DevTeam workspace MCP server is available in this session. Prefer using it to inspect current workspace state and to persist newly discovered issues, questions, and decisions. Use update_issue_status to set issue status instead of editing workspace files directly. Call get_runtime_capabilities for a full list of concerns the runtime manages automatically." : "No workspace MCP server is available in this session.")}

        Runtime-managed — do NOT create a QUESTION or ask the user about any of the following:
        - Budget, credit caps, or model tier selection: the runtime enforces these automatically.
        - Phase transitions (Planning → ArchitectPlanning → Execution): the runtime drives these.
        - Issue status updates: call update_issue_status (MCP) with status "in-progress", "done", or "blocked".
        - Run lifecycle (queuing, starting, completing runs): managed entirely by the runtime.
        - Pipeline stage chaining: the runtime chains architect → developer → tester automatically.
        - Workspace state file conflicts: trust the runtime's authoritative state; use update_issue_status to record your final status.
        - Closing, superseding, or deduplicating issues: make the call yourself and use update_issue_status to close superseded issues directly.
        - Retry or timeout decisions for other issues: the runtime schedules retries; just complete your own issue.
        - Whether another question can or should be closed: never ask the user to confirm closure of open questions.
        - Any "should I do X or Y?" where both options are within your role's authority: decide and act.

        QUESTIONS are only for information that cannot be inferred, that is genuinely required to proceed, and that only the end user can supply (e.g. target platform, business logic, secret credentials). If you can make a reasonable decision autonomously, do so.

        Pipeline handoff context:
        {BuildPipelineContextBlock(state, issue)}

        Open questions:
        {questionBlock}

        Recent decisions:
        {BuildDecisionBlock(state)}

        Valid role slugs for ISSUES:
        {availableRoles}
        {BuildFileBoundaryBlock(issue.RoleSlug)}
        Task:
        Work on the current issue using the available tools, active mode guardrails, and role guidance. Keep the scope narrow. If you discover follow-on work, a blocker, a prerequisite, or a natural decomposition that should not be absorbed into the current issue, use the workspace MCP tools when available and also summarize the outcome under ISSUES for compatibility. Avoid manually creating obvious next-stage architect, developer, or tester follow-ups for the same issue family when the runtime can chain those automatically. If you are blocked by missing information, say so clearly. Do not try to ask the user interactively. Instead, put every needed user question in the QUESTIONS section and use the workspace MCP tools when available to persist them immediately.

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

    public static string BuildAdHocPrompt(WorkspaceState state, string roleSlug, string userMessage)
    {
        var role = state.Roles.FirstOrDefault(item => string.Equals(item.Slug, roleSlug, StringComparison.OrdinalIgnoreCase));
        var activeMode = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, state.Runtime.ActiveModeSlug, StringComparison.OrdinalIgnoreCase))
            ?? state.Modes.FirstOrDefault();
        var roleBody = role?.Body ?? $"# Role: {roleSlug}";
        var superpowers = ResolveSuperpowers(state, roleSlug);
        var superpowerBlock = superpowers.Count == 0
            ? "(none)"
            : string.Join("\n\n---\n\n", superpowers.Select(item => item.Body.Trim()));
        var availableRoles = string.Join(", ", state.Roles.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

        return $"""
        You are working inside the DevTeam runtime. A human team member is addressing you directly.

        Current phase:
        {state.Phase}

        Active goal:
        {state.ActiveGoal?.GoalText ?? "(no active goal)"}

        Active mode:
        {(activeMode is null ? state.Runtime.ActiveModeSlug : $"{activeMode.Slug} ({activeMode.Name})")}

        Mode guardrails:
        {(activeMode?.Body.Trim() ?? "(none)")}

        Role instructions:
        {roleBody.Trim()}

        Relevant superpowers:
        {superpowerBlock}

        Workspace MCP:
        {(state.Runtime.WorkspaceMcpEnabled ? "A local DevTeam workspace MCP server is available in this session. Use it to inspect current workspace state and to persist newly discovered issues, questions, and decisions. Use update_issue_status to set issue status. Call get_runtime_capabilities to see what the runtime manages automatically." : "No workspace MCP server is available in this session.")}

        Runtime-managed — do NOT ask the user about: budget/model selection, phase transitions, issue status (use update_issue_status MCP), run lifecycle, pipeline chaining, workspace state file conflicts, closing or superseding issues (decide and act), or retry/timeout decisions for other issues. QUESTIONS are only for information only the end user can supply.

        Open questions:
        {BuildQuestionBlock(state)}

        Recent decisions:
        {BuildDecisionBlock(state)}

        Valid role slugs for ISSUES:
        {availableRoles}
        {BuildFileBoundaryBlock(roleSlug)}
        User message:
        {userMessage}

        Task:
        Respond to the user's message using your role expertise and the available tools. Be conversational and direct. If you discover work that should be tracked, propose it under ISSUES. If you need information from the user, put it under QUESTIONS.

        Reply in exactly this shape:
        OUTCOME: completed|blocked|failed
        SUMMARY:
        <your response to the user>
        ISSUES:
        - role=<role>; area=<area-or-none>; priority=<1-100>; depends=<ids-or-none>; title=<title>; detail=<detail>
        If no issues should be created, write `(none)` under ISSUES.
        QUESTIONS:
        - [blocking] <question text>
        - [non-blocking] <question text>
        If you do not need user input, write `(none)` under QUESTIONS.
        """;
    }

    public static string BuildOrchestratorPrompt(WorkspaceState state, IReadOnlyList<IssueItem> candidates, int maxSubagents)
    {
        var role = state.Roles.FirstOrDefault(item => string.Equals(item.Slug, "orchestrator", StringComparison.OrdinalIgnoreCase));
        var roleBody = role?.Body ?? "# Role: Orchestrator";
        var superpowers = ResolveSuperpowers(state, "orchestrator");
        var superpowerBlock = string.Join(
            "\n\n---\n\n",
            superpowers.Select(item => item.Body.Trim()));

        return $"""
        You are working inside the DevTeam runtime.

        Current phase:
        {state.Phase}

        Active goal:
        {state.ActiveGoal?.GoalText ?? "(no active goal)"}

        Task:
        Choose the next execution batch for the runtime. Select at most {maxSubagents} ready issue leads. Prefer architect-first sequencing when architecture is still unresolved, keep conflicting areas out of the same batch, and choose the smallest safe batch that keeps progress moving. Use the workspace MCP tools to inspect the workspace and persist your selection with `select_execution_batch`. Also repeat the selected issue ids under SELECTED_ISSUES for compatibility.

        Role instructions:
        {roleBody.Trim()}

        Relevant superpowers:
        {superpowerBlock}

        Execution candidates:
        {BuildCandidateBlock(candidates)}

        Open questions:
        {BuildQuestionBlock(state)}

        Recent decisions:
        {BuildDecisionBlock(state)}

        Reply in exactly this shape:
        OUTCOME: completed|blocked|failed
        SUMMARY:
        <short orchestration summary>
        SELECTED_ISSUES:
        - <issue id>
        If no issues should run, write `(none)` under SELECTED_ISSUES.
        ISSUES:
        - role=<role>; area=<shared-work-area-or-none>; priority=<1-100>; depends=<comma-separated existing issue ids or none>; title=<title>; detail=<detail>
        If no issues should be created, write `(none)` under ISSUES.
        SUPERPOWERS_USED:
        - <superpower slug>
        If none, write `(none)`.
        TOOLS_USED:
        - <tool name or command>
        If none, write `(none)`.
        QUESTIONS:
        - [blocking] <question text>
        - [non-blocking] <question text>
        If none, write `(none)`.
        """;
    }

    public static ParsedAgentResponse ParseResponse(AgentInvocationResult response)
    {
        if (!response.Success)
        {
            var error = string.IsNullOrWhiteSpace(response.StdErr) ? "Agent invocation failed." : response.StdErr.Trim();
            return new ParsedAgentResponse("failed", error, [], [], [], [], []);
        }

        var text = NormalizeStructuredResponse(response.StdOut).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedAgentResponse("completed", "Agent returned no summary.", [], [], [], [], []);
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var outcomeLine = lines.FirstOrDefault(line => line.StartsWith("OUTCOME:", StringComparison.OrdinalIgnoreCase));
        var summaryIndex = Array.FindIndex(lines, line => line.StartsWith("SUMMARY:", StringComparison.OrdinalIgnoreCase));
        var selectedIssuesIndex = Array.FindIndex(lines, line => line.StartsWith("SELECTED_ISSUES:", StringComparison.OrdinalIgnoreCase));
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

        var selectedIssueIds = ParseSelectedIssueIds(lines, selectedIssuesIndex, issuesIndex, superpowersIndex, toolsIndex, questionsIndex);
        var issues = ParseIssues(lines, issuesIndex, questionsIndex);
        var superpowersUsed = ParseSimpleList(lines, superpowersIndex, toolsIndex, questionsIndex);
        var toolsUsed = ParseSimpleList(lines, toolsIndex, questionsIndex);
        var questions = ParseQuestions(lines, questionsIndex);
        return new ParsedAgentResponse(
            outcome,
            string.IsNullOrWhiteSpace(summary) ? "No summary provided." : summary,
            selectedIssueIds,
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
        foreach (var header in new[] { "OUTCOME:", "SUMMARY:", "SELECTED_ISSUES:", "ISSUES:", "SUPERPOWERS_USED:", "TOOLS_USED:", "QUESTIONS:" })
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

    private static string BuildCandidateBlock(IReadOnlyList<IssueItem> candidates)
    {
        if (candidates.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            "\n",
            candidates.Select(item =>
                $"- #{item.Id} [{item.RoleSlug}] {item.Title} | priority={item.Priority} | area={(string.IsNullOrWhiteSpace(item.Area) ? "none" : item.Area)} | pipeline={(item.PipelineId?.ToString() ?? "none")} | stage={(item.PipelineStageIndex?.ToString() ?? "none")} | depends={(item.DependsOnIssueIds.Count == 0 ? "none" : string.Join(", ", item.DependsOnIssueIds))}"));
    }

    private static string BuildPipelineContextBlock(WorkspaceState state, IssueItem issue)
    {
        if (issue.PipelineId is null)
        {
            return "(this issue is not currently attached to a pipeline)";
        }

        var relatedIssues = state.Issues
            .Where(item => item.PipelineId == issue.PipelineId && item.Id != issue.Id)
            .OrderBy(item => item.PipelineStageIndex ?? int.MaxValue)
            .ThenBy(item => item.Id)
            .ToList();
        if (relatedIssues.Count == 0)
        {
            return "(this is the first known stage in the pipeline)";
        }

        var lines = new List<string>
        {
            $"Pipeline #{issue.PipelineId} for family {(string.IsNullOrWhiteSpace(issue.FamilyKey) ? "(none)" : issue.FamilyKey)}."
        };

        foreach (var relatedIssue in relatedIssues)
        {
            var latestRun = state.AgentRuns
                .Where(run => run.IssueId == relatedIssue.Id)
                .OrderByDescending(run => run.UpdatedAtUtc)
                .FirstOrDefault();
            var summary = latestRun is null || string.IsNullOrWhiteSpace(latestRun.Summary)
                ? "(no run summary yet)"
                : latestRun.Summary.Trim();
            lines.Add(
                $"- Stage {(relatedIssue.PipelineStageIndex?.ToString() ?? "?")} issue #{relatedIssue.Id} [{relatedIssue.RoleSlug}] {relatedIssue.Title} :: status={relatedIssue.Status}; summary={summary}");
        }

        lines.Add("Treat completed earlier stages as the current handoff contract unless you discover a concrete reason to revise them.");
        return string.Join("\n", lines);
    }

    private static readonly HashSet<string> ContextAwareRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "planner", "architect", "orchestrator", "navigator", "developer",
        "backend-developer", "frontend-developer", "fullstack-developer"
    };

    private static string BuildCodebaseContextBlock(WorkspaceState state, string roleSlug)
    {
        if (string.IsNullOrWhiteSpace(state.CodebaseContext)) return "";
        if (!ContextAwareRoles.Contains(roleSlug)) return "";

        return $"""


        Codebase context (produced by automatic reconnaissance — treat as factual):
        {state.CodebaseContext.Trim()}

        """;
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

    private static IReadOnlyList<int> ParseSelectedIssueIds(string[] lines, int sectionIndex, params int[] otherSectionIndexes)
    {
        if (sectionIndex < 0)
        {
            return [];
        }

        var endIndex = otherSectionIndexes
            .Where(index => index >= 0 && index > sectionIndex)
            .DefaultIfEmpty(lines.Length)
            .Min();
        var result = new List<int>();
        foreach (var rawLine in lines.Skip(sectionIndex + 1).Take(endIndex - sectionIndex - 1))
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
            if (int.TryParse(body, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result
            .Where(item => item > 0)
            .Distinct()
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
