using System.Text.RegularExpressions;

namespace DevTeam.Core;

public static class AgentPromptBuilder
{
    private const string NoneLiteral = "(none)";
    private const string OutcomeCompleted = "completed";
    private const string OutcomeBlocked = "blocked";
    private const string OutcomeFailed = "failed";
    private const string NoSummaryProvided = "No summary provided.";
    private const string AgentInvocationFailed = "Agent invocation failed.";
    private const string AgentReturnedNoSummary = "Agent returned no summary.";
    private const string RoleOrchestrator = "orchestrator";
    private const string RolePlanner = "planner";
    private const string RoleArchitect = "architect";
    private const string RoleNavigator = "navigator";
    private const string RoleDeveloper = "developer";
    private const string SkillBrainstorm = "brainstorm";
    private const string SkillPlan = "plan";
    private const string SkillScout = "scout";
    private const string SkillVerify = "verify";
    private const string HeaderOutcome = "OUTCOME:";
    private const string HeaderSummary = "SUMMARY:";
    private const string HeaderApproach = "APPROACH:";
    private const string HeaderRationale = "RATIONALE:";
    private const string HeaderSelectedIssues = "SELECTED_ISSUES:";
    private const string HeaderIssues = "ISSUES:";
    private const string HeaderSkillsUsed = "SKILLS_USED:";
    private const string HeaderToolsUsed = "TOOLS_USED:";
    private const string HeaderQuestions = "QUESTIONS:";

    private static readonly Dictionary<string, string[]> RoleSkillMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [RoleOrchestrator] = [SkillBrainstorm, SkillPlan],
        [RolePlanner] = [SkillBrainstorm, SkillPlan],
        [RoleArchitect] = [SkillBrainstorm, SkillPlan],
        [RoleNavigator] = [SkillScout],
        [RoleDeveloper] = [SkillPlan, "tdd", SkillVerify],
        ["backend-developer"] = [SkillPlan, "tdd", SkillVerify],
        ["frontend-developer"] = [SkillPlan, "tdd", SkillVerify],
        ["fullstack-developer"] = [SkillPlan, "tdd", SkillVerify],
        ["tester"] = ["tdd", "debug", SkillVerify],
        ["reviewer"] = ["review", SkillVerify],
        ["auditor"] = [SkillScout, "review", SkillVerify, "hygiene"],
        ["ux"] = [SkillVerify],
        ["user"] = [SkillVerify],
        ["game-designer"] = [SkillBrainstorm, "review", SkillVerify],
        ["conflict-resolver"] = ["resolve-conflict"]
    };

    private static readonly HashSet<string> DesignOnlyRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RolePlanner, RoleOrchestrator, RoleArchitect, RoleNavigator, "analyst", "security", "reviewer", "auditor"
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
        var skills = ResolveSkills(state, issue.RoleSlug);
        var questionBlock = BuildQuestionBlock(state);
        var tools = roleTools
            .Concat(skills.SelectMany(item => item.RequiredTools))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skillBlock = string.Join(
            "\n\n---\n\n",
            skills.Select(item => item.Body.Trim()));
        var availableRoles = string.Join(", ", state.Roles.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        var availableSkills = skills.Count == 0
            ? NoneLiteral
            : string.Join(", ", skills.Select(item => item.Slug).OrderBy(item => item, StringComparer.OrdinalIgnoreCase));

        return $"""
        You are working inside the DevTeam runtime.

        Current phase:
        {state.Phase}

        Active goal:
        {state.ActiveGoal?.GoalText ?? "(no active goal)"}

        Active mode:
        {(activeMode is null ? state.Runtime.ActiveModeSlug : $"{activeMode.Slug} ({activeMode.Name})")}

        Mode guardrails:
        {(activeMode?.Body.Trim() ?? NoneLiteral)}
        {BuildCodebaseContextBlock(state, issue.RoleSlug)}
        Current issue:
        - Id: {issue.Id}
        - Title: {issue.Title}
        - Detail: {issue.Detail}
        - Role: {issue.RoleSlug}
        - Area: {(string.IsNullOrWhiteSpace(issue.Area) ? NoneLiteral : issue.Area)}

        Role instructions:
        {roleBody.Trim()}

        Relevant skills:
        {skillBlock}

        Relevant skill slugs:
        {availableSkills}

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
        {BuildBrownfieldGuidanceBlock(state)}

        Reply in exactly this shape:
        OUTCOME: completed|blocked|failed
        SUMMARY:
        <short summary>
        {BuildBrownfieldReplyShape(state)}
        ISSUES:
        - role=<role>; area=<shared-work-area-or-none>; priority=<1-100>; depends=<comma-separated existing issue ids or none>; title=<title>; detail=<detail>
        If no issues should be created, write `(none)` under ISSUES.
        SKILLS_USED:
        - <skill slug>
        List only the skills you actually used from the provided guidance. If none, write `(none)`.
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
        var skills = ResolveSkills(state, roleSlug);
        var skillBlock = skills.Count == 0
            ? NoneLiteral
            : string.Join("\n\n---\n\n", skills.Select(item => item.Body.Trim()));
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
        {(activeMode?.Body.Trim() ?? NoneLiteral)}

        Role instructions:
        {roleBody.Trim()}

        Relevant skills:
        {skillBlock}

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
        var role = state.Roles.FirstOrDefault(item => string.Equals(item.Slug, RoleOrchestrator, StringComparison.OrdinalIgnoreCase));
        var roleBody = role?.Body ?? "# Role: Orchestrator";
        var skills = ResolveSkills(state, RoleOrchestrator);
        var skillBlock = string.Join(
            "\n\n---\n\n",
            skills.Select(item => item.Body.Trim()));

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

        Relevant skills:
        {skillBlock}

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
        SKILLS_USED:
        - <skill slug>
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
            var error = string.IsNullOrWhiteSpace(response.StdErr) ? AgentInvocationFailed : response.StdErr.Trim();
            return new ParsedAgentResponse(OutcomeFailed, error, "", "", [], [], [], [], []);
        }

        var text = NormalizeStructuredResponse(response.StdOut).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedAgentResponse(OutcomeCompleted, AgentReturnedNoSummary, "", "", [], [], [], [], []);
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sectionIndexes = FindSectionIndexes(lines);
        var outcome = ParseOutcome(sectionIndexes.OutcomeLine);
        var summary = ParseSummary(text, lines, sectionIndexes);
        var approach = sectionIndexes.ApproachIndex >= 0
            ? lines[sectionIndexes.ApproachIndex][HeaderApproach.Length..].Trim().ToLowerInvariant()
            : "";
        var rationale = ParseSection(lines, sectionIndexes.RationaleIndex, sectionIndexes.IssuesIndex, sectionIndexes.SkillsIndex, sectionIndexes.ToolsIndex, sectionIndexes.QuestionsIndex);
        var selectedIssueIds = ParseSelectedIssueIds(lines, sectionIndexes.SelectedIssuesIndex, sectionIndexes.IssuesIndex, sectionIndexes.SkillsIndex, sectionIndexes.ToolsIndex, sectionIndexes.QuestionsIndex);
        var issues = ParseIssues(lines, sectionIndexes.IssuesIndex, sectionIndexes.QuestionsIndex);
        var skillsUsed = ParseSimpleList(lines, sectionIndexes.SkillsIndex, sectionIndexes.ToolsIndex, sectionIndexes.QuestionsIndex);
        var toolsUsed = ParseSimpleList(lines, sectionIndexes.ToolsIndex, sectionIndexes.QuestionsIndex);
        var questions = ParseQuestions(lines, sectionIndexes.QuestionsIndex);
        return new ParsedAgentResponse(
            outcome,
            string.IsNullOrWhiteSpace(summary) ? NoSummaryProvided : summary,
            approach,
            rationale,
            selectedIssueIds,
            issues,
            skillsUsed,
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
        foreach (var header in StructuredHeaders)
        {
            normalized = Regex.Replace(
                normalized,
                $"(?<!^)(?<!\\n)({Regex.Escape(header)})",
                "\n$1",
                RegexOptions.CultureInvariant);
        }

        return normalized;
    }

    private static List<SkillDefinition> ResolveSkills(WorkspaceState state, string roleSlug)
    {
        if (!RoleSkillMap.TryGetValue(roleSlug, out var slugs))
        {
            return [];
        }

        return state.Skills
            .Where(item => slugs.Contains(item.Slug, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static string BuildQuestionBlock(WorkspaceState state)
    {
        var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
        if (openQuestions.Count == 0)
        {
            return NoneLiteral;
        }

        return string.Join(
            "\n",
            openQuestions.Select(item => $"- #{item.Id} [{(item.IsBlocking ? "blocking" : "non-blocking")}] {item.Text}"));
    }

    private static string BuildBrownfieldGuidanceBlock(WorkspaceState state)
    {
        if (string.IsNullOrWhiteSpace(state.CodebaseContext))
        {
            return "";
        }

        return """

        Brownfield delta:
        You are working in an existing codebase with prior patterns and constraints. Use the project map/codebase context as the baseline for what already exists. In addition to the summary, explain how you handled the existing code:
        - APPROACH must be one of: extend, replace, workaround
        - RATIONALE should explain why that approach fit the existing codebase
        Keep this decision scoped to the current issue only.
        """;
    }

    private static string BuildBrownfieldReplyShape(WorkspaceState state)
    {
        if (string.IsNullOrWhiteSpace(state.CodebaseContext))
        {
            return "";
        }

        return """
        APPROACH: extend|replace|workaround
        RATIONALE:
        <one short rationale about how you handled the existing codebase>
        """;
    }

    private static string BuildDecisionBlock(WorkspaceState state)
    {
        var recentDecisions = state.Decisions
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(5)
            .ToList();
        if (recentDecisions.Count == 0)
        {
            return NoneLiteral;
        }

        return string.Join(
            "\n",
            recentDecisions.Select(item => $"- #{item.Id} [{item.Source}] {item.Title}: {item.Detail}"));
    }

    private static string ParseSection(
        string[] lines,
        int startIndex,
        params int[] endIndexes)
    {
        if (startIndex < 0)
        {
            return "";
        }

        var endIndex = endIndexes
            .Where(index => index >= 0 && index > startIndex)
            .DefaultIfEmpty(lines.Length)
            .Min();
        var inlineContent = lines[startIndex].Split(':', 2).Skip(1).FirstOrDefault()?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(inlineContent))
        {
            return inlineContent;
        }

        return string.Join('\n', lines.Skip(startIndex + 1).Take(endIndex - startIndex - 1)).Trim();
    }

    private static string BuildCandidateBlock(IReadOnlyList<IssueItem> candidates)
    {
        if (candidates.Count == 0)
        {
            return NoneLiteral;
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
            $"Pipeline #{issue.PipelineId} for family {(string.IsNullOrWhiteSpace(issue.FamilyKey) ? NoneLiteral : issue.FamilyKey)}."
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
        RolePlanner, RoleArchitect, RoleOrchestrator, RoleNavigator, RoleDeveloper,
        "backend-developer", "frontend-developer", "fullstack-developer"
    };

    private static readonly string[] StructuredHeaders =
    [
        HeaderOutcome,
        HeaderSummary,
        HeaderApproach,
        HeaderRationale,
        HeaderSelectedIssues,
        HeaderIssues,
        HeaderSkillsUsed,
        HeaderToolsUsed,
        HeaderQuestions
    ];

    private static StructuredSectionIndexes FindSectionIndexes(string[] lines) => new(
        lines.FirstOrDefault(line => line.StartsWith(HeaderOutcome, StringComparison.OrdinalIgnoreCase)),
        FindHeaderIndex(lines, HeaderSummary),
        FindHeaderIndex(lines, HeaderApproach),
        FindHeaderIndex(lines, HeaderRationale),
        FindHeaderIndex(lines, HeaderSelectedIssues),
        FindHeaderIndex(lines, HeaderIssues),
        FindHeaderIndex(lines, HeaderSkillsUsed),
        FindHeaderIndex(lines, HeaderToolsUsed),
        FindHeaderIndex(lines, HeaderQuestions));

    private static int FindHeaderIndex(string[] lines, string header) =>
        Array.FindIndex(lines, line => line.StartsWith(header, StringComparison.OrdinalIgnoreCase));

    private static string ParseOutcome(string? outcomeLine)
    {
        if (string.IsNullOrWhiteSpace(outcomeLine))
        {
            return OutcomeCompleted;
        }

        var outcome = outcomeLine[HeaderOutcome.Length..].Trim().ToLowerInvariant();
        return outcome is OutcomeCompleted or OutcomeBlocked or OutcomeFailed
            ? outcome
            : OutcomeCompleted;
    }

    private static string ParseSummary(string text, string[] lines, StructuredSectionIndexes sectionIndexes)
    {
        if (sectionIndexes.SummaryIndex < 0)
        {
            return text;
        }

        var summaryEndIndex = GetSectionEndIndex(
            sectionIndexes.SummaryIndex,
            lines.Length,
            sectionIndexes.ApproachIndex,
            sectionIndexes.RationaleIndex,
            sectionIndexes.IssuesIndex,
            sectionIndexes.SkillsIndex,
            sectionIndexes.ToolsIndex,
            sectionIndexes.QuestionsIndex);
        var tail = lines.Skip(sectionIndexes.SummaryIndex + 1).Take(summaryEndIndex - sectionIndexes.SummaryIndex - 1).ToList();
        return tail.Count == 0
            ? NoSummaryProvided
            : string.Join('\n', tail).Trim();
    }

    private static string BuildCodebaseContextBlock(WorkspaceState state, string roleSlug)
    {
        if (string.IsNullOrWhiteSpace(state.CodebaseContext)) return "";
        if (!ContextAwareRoles.Contains(roleSlug)) return "";

        return $"""


        Project map / codebase context (produced by automatic reconnaissance — treat as factual):
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
            if (IsNoneOrBlank(line))
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

        var endIndex = GetSectionEndIndex(sectionIndex, lines.Length, otherSectionIndexes);
        var result = new List<string>();
        foreach (var rawLine in lines.Skip(sectionIndex + 1).Take(endIndex - sectionIndex - 1))
        {
            var line = rawLine.Trim();
            if (IsNoneOrBlank(line))
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

        var endIndex = GetSectionEndIndex(sectionIndex, lines.Length, otherSectionIndexes);
        var result = new List<int>();
        foreach (var rawLine in lines.Skip(sectionIndex + 1).Take(endIndex - sectionIndex - 1))
        {
            var line = rawLine.Trim();
            if (IsNoneOrBlank(line))
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

        var endIndex = GetSectionEndIndex(
            issuesIndex,
            lines.Length,
            FindHeaderIndex(lines, HeaderSkillsUsed),
            FindHeaderIndex(lines, HeaderToolsUsed),
            questionsIndex);
        var result = new List<GeneratedIssueProposal>();
        foreach (var rawLine in lines.Skip(issuesIndex + 1).Take(endIndex - issuesIndex - 1))
        {
            var line = rawLine.Trim();
            if (IsNoneOrBlank(line) || !line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var proposal = ParseIssueProposal(line[1..].Trim());
            if (proposal is not null)
            {
                result.Add(proposal);
            }
        }

        return result;
    }

    private static GeneratedIssueProposal? ParseIssueProposal(string body)
    {
        var values = ParseIssueFields(body);
        if (!values.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        values.TryGetValue("role", out var role);
        values.TryGetValue("area", out var area);
        values.TryGetValue("detail", out var detail);
        var priority = values.TryGetValue("priority", out var priorityText) && int.TryParse(priorityText, out var parsedPriority)
            ? parsedPriority
            : 50;

        return new GeneratedIssueProposal
        {
            Title = title.Trim(),
            Detail = detail?.Trim() ?? "",
            RoleSlug = string.IsNullOrWhiteSpace(role) ? RoleDeveloper : role.Trim(),
            Area = string.Equals(area, "none", StringComparison.OrdinalIgnoreCase) ? "" : area?.Trim() ?? "",
            Priority = priority,
            DependsOnIssueIds = ParseDependencyIds(values)
        };
    }

    private static Dictionary<string, string> ParseIssueFields(string body)
    {
        var fields = body.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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

        return values;
    }

    private static List<int> ParseDependencyIds(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("depends", out var dependsText))
        {
            return [];
        }

        return dependsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
            .Where(value => value > 0)
            .ToList();
    }

    private static bool IsNoneOrBlank(string line) =>
        string.IsNullOrWhiteSpace(line) || string.Equals(line, NoneLiteral, StringComparison.OrdinalIgnoreCase);

    private static int GetSectionEndIndex(int startIndex, int defaultEndIndex, params int[] candidateIndexes) =>
        candidateIndexes
            .Where(index => index >= 0 && index > startIndex)
            .DefaultIfEmpty(defaultEndIndex)
            .Min();

    private readonly record struct StructuredSectionIndexes(
        string? OutcomeLine,
        int SummaryIndex,
        int ApproachIndex,
        int RationaleIndex,
        int SelectedIssuesIndex,
        int IssuesIndex,
        int SkillsIndex,
        int ToolsIndex,
        int QuestionsIndex);
}
