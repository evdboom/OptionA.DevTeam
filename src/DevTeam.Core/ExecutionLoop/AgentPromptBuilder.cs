using System.Text;
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
    private const string RoleReviewer = "reviewer";
    private const string RoleAuditor = "auditor";
    private const string SkillBrainstorm = "brainstorm";
    private const string SkillPlan = "plan";
    private const string SkillScout = "scout";
    private const string SkillVerify = "verify";
    private const string SkillTdd = "tdd";
    private const string SkillDebug = "debug";
    private const string SkillReview = "review";
    private const string SkillHygiene = "hygiene";
    private const string SkillBacklogManager = "backlog-manager";
    private const string SkillRefine = "refine";
    private const string SkillWorkspaceProtection = "workspace-protection";
    private const string HeaderOutcome = "OUTCOME:";
    private const string HeaderSummary = "SUMMARY:";
    private const string HeaderApproach = "APPROACH:";
    private const string HeaderRationale = "RATIONALE:";
    private const string HeaderSelectedIssues = "SELECTED_ISSUES:";
    private const string HeaderIssues = "ISSUES:";
    private const string HeaderSkillsUsed = "SKILLS_USED:";
    private const string HeaderToolsUsed = "TOOLS_USED:";
    private const string HeaderQuestions = "QUESTIONS:";
    private const int RoleExcerptMaxLines = 12;

    private static readonly Dictionary<string, string[]> RoleSkillMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [RoleOrchestrator] = [SkillBrainstorm, SkillPlan, SkillBacklogManager, SkillWorkspaceProtection],
        [RolePlanner] = [SkillBrainstorm, SkillPlan],
        [RoleArchitect] = [SkillBrainstorm, SkillPlan, SkillRefine],
        [RoleNavigator] = [SkillScout, SkillRefine],
        [RoleDeveloper] = [SkillPlan, SkillTdd, SkillVerify, SkillWorkspaceProtection],
        ["backend-developer"] = [SkillPlan, SkillTdd, SkillVerify, SkillWorkspaceProtection],
        ["frontend-developer"] = [SkillPlan, SkillTdd, SkillVerify, SkillWorkspaceProtection],
        ["fullstack-developer"] = [SkillPlan, SkillTdd, SkillVerify, SkillWorkspaceProtection],
        ["tester"] = [SkillTdd, SkillDebug, SkillVerify, SkillWorkspaceProtection],
        [RoleReviewer] = [SkillReview, SkillVerify, SkillRefine, SkillWorkspaceProtection],
        [RoleAuditor] = [SkillScout, SkillReview, SkillVerify, SkillHygiene, SkillRefine, SkillWorkspaceProtection],
        ["ux"] = [SkillVerify],
        ["user"] = [SkillVerify],
        ["game-designer"] = [SkillBrainstorm, "review", SkillVerify],
        ["conflict-resolver"] = ["resolve-conflict"]
    };

    private static readonly HashSet<string> DesignOnlyRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RolePlanner, RoleOrchestrator, RoleArchitect, RoleNavigator, "analyst", "security", RoleReviewer, RoleAuditor
    };

    private static readonly HashSet<string> WideResearchRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RolePlanner, RoleOrchestrator, RoleArchitect, RoleNavigator, "analyst", "security", RoleReviewer, RoleAuditor
    };

    private static readonly HashSet<string> ScopedExecutionRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleDeveloper, "backend-developer", "frontend-developer", "fullstack-developer", "tester", "devops", "docs", "refactorer"
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

    public static string BuildPrompt(WorkspaceState state, IssueItem issue, string? contextHint = null)
        => BuildPrompt(state, issue, TimeSpan.FromMinutes(10), contextHint);

    public static string BuildPrompt(WorkspaceState state, IssueItem issue, TimeSpan agentTimeout, string? contextHint = null)
    {
        var role = state.Roles.FirstOrDefault(item => item.Slug == issue.RoleSlug);
        var activeMode = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, state.Runtime.ActiveModeSlug, StringComparison.OrdinalIgnoreCase))
            ?? state.Modes.FirstOrDefault();
        var roleCard = BuildRoleCard(role, issue.RoleSlug);
        var skills = ResolveSkills(state, issue.RoleSlug, issue);
        var questionBlock = BuildQuestionBlock(state);

        var skillManifest = BuildSkillManifest(skills);
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
        {BuildExecutionScopeBlock(issue.RoleSlug)}
        {BuildContextHintBlock(contextHint)}

        Role baseline:
        {roleCard}

        Skill manifest (load on demand):
        {skillManifest}

        Skill-loading rule:
        Only load skill instructions when needed for the current step. Do not preload every skill.
        Load a skill by reading its SourcePath using available tools, then follow only the relevant section.

        Relevant skill slugs:
        {availableSkills}

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
        - Whether issue #X or #Y are completed, missing from candidate lists, or blocking another issue: inspect workspace state and resolve internally; never ask the user.
        - Whether another question can or should be closed: never ask the user to confirm closure of open questions.
        - Any "should I do X or Y?" where both options are within your role's authority: decide and act.

        QUESTIONS are only for information that cannot be inferred, that is genuinely required to proceed, and that only the end user can supply (e.g. target platform, business logic, secret credentials). If you can make a reasonable decision autonomously, do so.
        Every question must be self-contained. Do not reference "above", "the previous line", or implied context without restating the concrete evidence in the same question entry.
        Non-blocking runtime/scheduling questions (timeouts, batching, ordering, retries, split strategy, issue closure policy) are auto-resolved by runtime policy and should not be emitted.

        Pipeline handoff context:
        {BuildPipelineContextBlock(state, issue)}

        Open questions:
        {questionBlock}

        Recent decisions:
        {BuildDecisionBlock(state)}

        Planning feedback to apply:
        {BuildPlanningFeedbackBlock(state)}

        Valid role slugs for ISSUES:
        {availableRoles}
        {BuildAuditorBoundaryBlock(issue.RoleSlug)}
        {BuildDesignRoleTestabilityBlock(issue.RoleSlug)}
        {BuildFileBoundaryBlock(issue.RoleSlug)}

        Critical Workspace Protection:
        The `.devteam/` directory contains runtime state (workspace.json, decisions, runs, checkpoints).
        ⚠️ DO NOT delete or corrupt .devteam/ with commands like: git restore . | git clean -fd | git reset --hard
        Use git restore <specific-path> instead of git restore . (with no args).
        Use git clean only after cd'ing to a safe subdirectory.
        Load the workspace-protection skill if you need to run git commands in the repo root.
        If you accidentally delete .devteam, the runtime will recover from checkpoint but log a GUARDRAIL VIOLATION.
        Multiple violations = systematic problem that must be fixed.

        Time budget:
        You have a hard session timeout of {(int)agentTimeout.TotalMinutes} minutes. The runtime will hard-kill the session when that limit is reached — you will not get a chance to reply.
        Before you start: estimate whether the full scope of this issue fits within that budget.
        If you are nearly done but need a few more minutes: call request_timeout_extension (MCP) with the current issueId. One extension per run is available. You will not receive a confirmation — the runtime grants it silently and you gain the runtime-configured extra time.
        If the full scope does not fit: complete the highest-value subset that does fit, then emit split follow-up ISSUES for the remaining work. Never let work silently disappear — hand off explicitly.

        Task:
        Work on the current issue using the available tools, active mode guardrails, and role guidance. Keep the scope narrow.
        First perform a fit check: if this issue is unlikely to fit one focused run, complete the safest meaningful subset and emit split follow-up ISSUES rather than timing out.
        If "Planning feedback to apply" is non-empty, treat it as an explicit revision request and address it directly in this run rather than redoing a generic plan.
        If you discover follow-on work, a blocker, a prerequisite, or a natural decomposition that should not be absorbed into the current issue, use the workspace MCP tools when available and also summarize the outcome under ISSUES for compatibility.
        Avoid manually creating obvious next-stage architect, developer, or tester follow-ups for the same issue family when the runtime can chain those automatically.
        If you are blocked by missing information, say so clearly. Do not try to ask the user interactively. Instead, put every needed user question in the QUESTIONS section and use the workspace MCP tools when available to persist them immediately.
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
                - [blocking] <self-contained question text>
                    context: <optional supporting facts from this run>
                - [non-blocking] <self-contained question text>
                    context: <optional supporting facts from this run>
        If you do not need user input, write `(none)` under QUESTIONS.
        """;
    }

    public static string BuildAdHocPrompt(WorkspaceState state, string roleSlug, string userMessage)
    {
        var role = state.Roles.FirstOrDefault(item => string.Equals(item.Slug, roleSlug, StringComparison.OrdinalIgnoreCase));
        var activeMode = state.Modes.FirstOrDefault(item => string.Equals(item.Slug, state.Runtime.ActiveModeSlug, StringComparison.OrdinalIgnoreCase))
            ?? state.Modes.FirstOrDefault();
        var roleCard = BuildRoleCard(role, roleSlug);
        var skills = ResolveSkills(state, roleSlug);
        var skillManifest = BuildSkillManifest(skills);
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

        Role baseline:
        {roleCard}

        Skill manifest (load on demand):
        {skillManifest}

        Skill-loading rule:
        Only load skill instructions when needed for the current step. Do not preload every skill.

        Workspace MCP:
        {(state.Runtime.WorkspaceMcpEnabled ? "A local DevTeam workspace MCP server is available in this session. Use it to inspect current workspace state and to persist newly discovered issues, questions, and decisions. Use update_issue_status to set issue status. Call get_runtime_capabilities to see what the runtime manages automatically." : "No workspace MCP server is available in this session.")}

        Runtime-managed — do NOT ask the user about: budget/model selection, phase transitions, issue status (use update_issue_status MCP), run lifecycle, pipeline chaining, workspace state file conflicts, closing or superseding issues (decide and act), or retry/timeout decisions for other issues. QUESTIONS are only for information only the end user can supply.
        Every question must be self-contained. Do not reference "above" or implied context without restating the concrete evidence in the same question entry.

        Open questions:
        {BuildQuestionBlock(state)}

        Recent decisions:
        {BuildDecisionBlock(state)}

        Valid role slugs for ISSUES:
        {availableRoles}
        {BuildExecutionScopeBlock(roleSlug)}
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
                - [blocking] <self-contained question text>
                    context: <optional supporting facts from this run>
                - [non-blocking] <self-contained question text>
                    context: <optional supporting facts from this run>
        If you do not need user input, write `(none)` under QUESTIONS.
        """;
    }

    public static string BuildOrchestratorPrompt(WorkspaceState state, IReadOnlyList<IssueItem> candidates, int maxSubagents)
    {
        var role = state.Roles.FirstOrDefault(item => string.Equals(item.Slug, RoleOrchestrator, StringComparison.OrdinalIgnoreCase));
        var roleCard = BuildRoleCard(role, RoleOrchestrator);
        var skills = ResolveSkills(state, RoleOrchestrator);
        var skillManifest = BuildSkillManifest(skills);

        return $"""
        You are working inside the DevTeam runtime.

        Current phase:
        {state.Phase}

        Active goal:
        {state.ActiveGoal?.GoalText ?? "(no active goal)"}

        Task:
        Choose the next execution batch for the runtime. Select at most {maxSubagents} ready issue leads. Prefer architect-first sequencing when architecture is still unresolved, keep conflicting areas out of the same batch, and choose the smallest safe batch that keeps progress moving. Use the workspace MCP tools to inspect the workspace and persist your selection with `select_execution_batch`. Also repeat the selected issue ids under SELECTED_ISSUES for compatibility.
        Route implementation work to specialized roles whenever clear: `frontend-developer` for UI/Blazor/client, `backend-developer` for API/data/server, `fullstack-developer` only when one issue must span both sides. Use base `developer` only when specialization is unclear.
        Reviewer and auditor are guardrail roles. Runtime may auto-inject these based on change footprint/cadence, so avoid duplicate guardrail issues unless you need a distinct scoped follow-up.

        Role baseline:
        {roleCard}

        Skill manifest (load on demand):
        {skillManifest}

        Skill-loading rule:
        Load only the skill files needed for the current scheduling decision.

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
                - [blocking] <self-contained question text>
                    context: <optional supporting facts from this run>
                - [non-blocking] <self-contained question text>
                    context: <optional supporting facts from this run>
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

    private static List<SkillDefinition> ResolveSkills(WorkspaceState state, string roleSlug, IssueItem? issue = null)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (RoleSkillMap.TryGetValue(roleSlug, out var roleSlugs))
        {
            foreach (var slug in roleSlugs)
            {
                slugs.Add(slug);
            }
        }

        if (issue is not null)
        {
            foreach (var inferredSlug in InferSkillSlugsForIssue(issue))
            {
                slugs.Add(inferredSlug);
            }
        }

        if (slugs.Count == 0)
        {
            return [];
        }

        return state.Skills
            .Where(item => slugs.Contains(item.Slug))
            .ToList();
    }

    private static IReadOnlyList<string> InferSkillSlugsForIssue(IssueItem issue)
    {
        var text = $"{issue.Title} {issue.Detail}";
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ContainsAny(text, "test", "unit", "integration", "regression", "coverage"))
        {
            slugs.Add(SkillTdd);
            slugs.Add(SkillVerify);
        }

        if (ContainsAny(text, "debug", "exception", "error", "timeout", "flake"))
        {
            slugs.Add(SkillDebug);
            slugs.Add(SkillVerify);
        }

        if (ContainsAny(text, "review", "audit", "security", "maintainability", "hygiene"))
        {
            slugs.Add(SkillReview);
            slugs.Add(SkillHygiene);
        }

        if (ContainsAny(text, "scout", "recon", "map", "inventory"))
        {
            slugs.Add(SkillScout);
        }

        if (ContainsAny(text, "plan", "design", "architecture", "decompose", "split"))
        {
            slugs.Add(SkillPlan);
        }

        if (ContainsAny(text, "refine", "refinement", "scope", "filesinscope", "linkeddecision", "triage", "what why how"))
        {
            slugs.Add(SkillRefine);
        }

        if (ContainsAny(text, "git", "restore", "clean", "reset", "checkout", ".devteam", "workspace state", "workspace.json", "checkpoint"))
        {
            slugs.Add(SkillWorkspaceProtection);
        }

        return slugs.ToList();
    }

    private static bool ContainsAny(string source, params string[] needles) =>
        needles.Any(needle => source.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string BuildExecutionScopeBlock(string roleSlug)
    {
        if (ScopedExecutionRoles.Contains(roleSlug))
        {
            return """

        EXECUTION SCOPE (scoped role):
        - You are a scoped execution role. Start from the current issue only.
        - Before implementation, fetch scoped context via MCP:
          1) call get_issue(issueId)
          2) call get_decisions(linkedDecisionIds) from that issue
        - Stay focused on FilesInScope when provided. Expand only for direct dependencies discovered during work.
        - If scope is insufficient or ambiguous, create a refinement issue instead of reinterpreting the goal.
        - Refinement issues must be exhaustive: include what, why, how, suggested FilesInScope, and linked decision IDs.
        """;
        }

        if (WideResearchRoles.Contains(roleSlug))
        {
            return """

        EXECUTION SCOPE (wide research role):
        - You may read broadly across the repository to map architecture, dependencies, and risks.
        - Do not perform broad implementation; produce scoped follow-up issues for execution roles.
        - Issues you create must be exhaustive and testable: include what, why, how, acceptance criteria, FilesInScope, and linked decisions.
        """;
        }

        return "";
    }

    private static string BuildContextHintBlock(string? contextHint)
    {
        if (string.IsNullOrWhiteSpace(contextHint))
        {
            return "";
        }

        return $"""

        Supplemental caller context:
        {contextHint.Trim()}

        Treat this as a convenience hint from the caller, not as authoritative scope.
        The issue record and linked decisions remain the source of truth.
        Use this to avoid re-discovering context that is not yet captured in the issue, but do not let it override decisions or broaden scope casually.
        """;
    }

    private static string BuildDesignRoleTestabilityBlock(string roleSlug)
    {
        if (!DesignOnlyRoles.Contains(roleSlug))
        {
            return "";
        }

        return """

        DESIGN TESTABILITY CONTRACT (for issues you create):
        - Require constructor injection for non-trivial collaborators.
        - Call out explicit abstractions for file system dependencies.
        - Call out explicit abstractions for clock dependencies.
        """;
    }

    private static string BuildAuditorBoundaryBlock(string roleSlug)
    {
        if (!string.Equals(roleSlug, RoleAuditor, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return """

        AUDITOR BOUNDARIES:
        - Focus on legacy drift, recent drift, and active regression risk.
        - Reviewer handles line-level code quality checks; do not duplicate Reviewer scope.
        - Navigator handles codebase mapping/scouting; do not duplicate Navigator scope.
        - Security handles deep threat modeling/compliance; do not duplicate Security scope.
        """;
    }

    private static string BuildRoleCard(RoleDefinition? role, string roleSlug)
    {
        if (role is null)
        {
            return $"- slug: {roleSlug}\n- source: (runtime default)\n- guidance: no custom role file found.";
        }

        var excerpt = ExtractRoleExcerpt(role.Body, RoleExcerptMaxLines);
        var indentedExcerpt = excerpt.Replace("\n", "\n    ", StringComparison.Ordinal);

                return $"""
        - slug: {role.Slug}
        - source: {role.SourcePath}
        - excerpt:
                        {indentedExcerpt}
        """;
    }

    private static string ExtractRoleExcerpt(string body, int maxLines)
    {
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
            .Take(maxLines)
            .ToList();
        if (lines.Count == 0)
        {
            return "(empty role guidance)";
        }

        return string.Join("\n", lines);
    }

    private static string BuildSkillManifest(IReadOnlyList<SkillDefinition> skills)
    {
        if (skills.Count == 0)
        {
            return NoneLiteral;
        }

        var builder = new StringBuilder();
        foreach (var skill in skills.OrderBy(item => item.Slug, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("- slug=")
                .Append(skill.Slug)
                .Append("; name=")
                .Append(string.IsNullOrWhiteSpace(skill.Name) ? skill.Slug : skill.Name)
                .Append("; source=")
                .Append(string.IsNullOrWhiteSpace(skill.SourcePath) ? "(unknown)" : skill.SourcePath)
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildQuestionBlock(WorkspaceState state)
    {
        var openQuestions = state.Questions.Where(item => item.Status == QuestionStatus.Open).ToList();
        if (openQuestions.Count == 0)
        {
            return NoneLiteral;
        }

        return string.Join("\n", openQuestions.Select(FormatQuestionForPrompt));
    }

    private static string FormatQuestionForPrompt(QuestionItem question)
    {
        var prefix = $"- #{question.Id} [{(question.IsBlocking ? "blocking" : "non-blocking")}] ";
        var lines = SplitQuestionLines(question.Text);
        if (lines.Count == 0)
        {
            return prefix.TrimEnd();
        }

        if (lines.Count == 1)
        {
            return prefix + lines[0];
        }

        var builder = new StringBuilder();
        builder.AppendLine(prefix + lines[0]);
        foreach (var line in lines.Skip(1))
        {
            builder.Append("  ").AppendLine(line);
        }

        return builder.ToString().TrimEnd();
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

    private static string BuildPlanningFeedbackBlock(WorkspaceState state)
    {
        var planningFeedback = state.Decisions
            .Where(item =>
                string.Equals(item.Source, "plan-feedback", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Source, "architect-plan-feedback", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(5)
            .ToList();

        if (planningFeedback.Count == 0)
        {
            return NoneLiteral;
        }

        return string.Join(
            "\n",
            planningFeedback.Select(item => $"- [{item.Source}] {item.Detail}"));
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
        ProposedQuestion? current = null;
        var contextLines = new List<string>();
        foreach (var rawLine in lines.Skip(questionsIndex + 1))
        {
            var line = rawLine.Trim();
            if (IsNoneOrBlank(line))
            {
                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                if (current is not null && !string.IsNullOrWhiteSpace(line))
                {
                    contextLines.Add(line);
                }
                continue;
            }

            if (current is not null)
            {
                result.Add(AppendQuestionContext(current, contextLines));
                current = null;
                contextLines.Clear();
            }

            var body = line[1..].Trim();
            if (body.StartsWith("[blocking]", StringComparison.OrdinalIgnoreCase))
            {
                current = new ProposedQuestion
                {
                    IsBlocking = true,
                    Text = body["[blocking]".Length..].Trim()
                };
            }
            else if (body.StartsWith("[non-blocking]", StringComparison.OrdinalIgnoreCase))
            {
                current = new ProposedQuestion
                {
                    IsBlocking = false,
                    Text = body["[non-blocking]".Length..].Trim()
                };
            }
        }

        if (current is not null)
        {
            result.Add(AppendQuestionContext(current, contextLines));
        }

        return result;
    }

    private static ProposedQuestion AppendQuestionContext(ProposedQuestion question, IReadOnlyList<string> contextLines)
    {
        if (contextLines.Count == 0)
        {
            return question;
        }

        var context = string.Join('\n', contextLines)
            .Trim();
        if (string.IsNullOrWhiteSpace(context))
        {
            return question;
        }

        return new ProposedQuestion
        {
            IsBlocking = question.IsBlocking,
            Text = $"{question.Text.Trim()}\nContext: {context}"
        };
    }

    private static IReadOnlyList<string> SplitQuestionLines(string text)
        => text.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

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
