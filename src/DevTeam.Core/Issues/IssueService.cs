using System.Diagnostics.CodeAnalysis;

namespace DevTeam.Core;

[SuppressMessage("Major Code Smell", "S1192", Justification = "Role aliases intentionally repeat canonical slugs for readability.")]
[SuppressMessage("Major Code Smell", "S107", Justification = "Compatibility overloads preserve legacy AddIssue shape.")]
[SuppressMessage("Major Code Smell", "S4144", Justification = "Role resolution methods remain split for API clarity.")]
[SuppressMessage("Minor Code Smell", "S3267", Justification = "Explicit loops keep pipeline selection guardrails readable.")]
public sealed partial class IssueService : IIssueService
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

    private readonly ISystemClock _clock;

    public IssueService(ISystemClock? clock = null)
    {
        _clock = clock ?? new SystemClock();
    }

    public static IssueItem AddIssue(WorkspaceState state, string title, string detail, string roleSlug,
        int priority, int? roadmapItemId, IEnumerable<int> dependsOn, string? area = null,
        string? familyKey = null, int? parentIssueId = null, int? pipelineId = null,
        int? pipelineStageIndex = null, int? complexityHint = null)
        => CreateIssueCore(
            state,
            new IssueRequest
            {
                Title = title,
                Detail = detail,
                RoleSlug = roleSlug,
                Priority = priority,
                RoadmapItemId = roadmapItemId,
                DependsOn = dependsOn,
                Area = area,
                FamilyKey = familyKey,
                ParentIssueId = parentIssueId,
                PipelineId = pipelineId,
                PipelineStageIndex = pipelineStageIndex,
                ComplexityHint = complexityHint
            });

    public IssueItem CreateIssue(WorkspaceState state, IssueRequest request)
        => CreateIssueCore(state, request);

    public IssueItem? FindIssue(WorkspaceState state, int issueId)
        => state.Issues.FirstOrDefault(i => i.Id == issueId);

    public IReadOnlyList<IssueItem> GetReadyIssues(WorkspaceState state, int maxCount)
    {
        var readyLeads = GetReadyIssueCandidates(state);
        var activeRuns = state.AgentRuns
            .Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running)
            .ToList();
        var reservedAreas = activeRuns
            .Select(run => state.Issues.FirstOrDefault(issue => issue.Id == run.IssueId)?.Area)
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reservedPipelineIds = activeRuns
            .Select(run => state.Issues.FirstOrDefault(issue => issue.Id == run.IssueId)?.PipelineId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();
        var desiredConcurrency = DeterminePipelineConcurrency(state, readyLeads, maxCount);

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

    public IReadOnlyList<IssueItem> GetReadyIssueCandidates(WorkspaceState state)
    {
        var activeRuns = state.AgentRuns
            .Where(run => run.Status is AgentRunStatus.Queued or AgentRunStatus.Running)
            .ToList();
        var queuedIssueIds = activeRuns
            .Select(run => run.IssueId)
            .ToHashSet();

        var ready = state.Issues
            .Where(issue => issue.Status == ItemStatus.Open)
            .Where(issue => !queuedIssueIds.Contains(issue.Id))
            .Where(issue => IsIssueEligibleForPhase(state.Phase, issue))
            .Where(issue => issue.DependsOnIssueIds.All(depId => state.Issues.Any(dep => dep.Id == depId && dep.Status == ItemStatus.Done)))
            .OrderByDescending(issue => issue.Priority)
            .ThenBy(issue => issue.Id)
            .ToList();

        foreach (var issue in ready)
        {
            EnsurePipelineForIssue(state, issue);
        }

        if (state.Phase == WorkflowPhase.Execution
            && ready.Any(issue => string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase)))
        {
            ready = ready
                .Where(issue => string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase))
                .ToList();
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
        return readyLeads;
    }

    public void EnsurePipelineAssignments(WorkspaceState state)
    {
        foreach (var issue in state.Issues.Where(item => !item.IsPlanningIssue))
        {
            EnsurePipelineForIssue(state, issue);
            NormalizePipelineFollowUpIssue(state, issue);
        }
    }

    public void AdvancePipelineAfterCompletion(WorkspaceState state, IssueItem issue)
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
            pipeline.UpdatedAtUtc = _clock.UtcNow;
            return;
        }

        var nextStageIndex = currentStage + 1;
        var nextIssue = state.Issues.FirstOrDefault(item =>
            item.PipelineId == pipeline.Id && item.PipelineStageIndex == nextStageIndex);
        if (nextIssue is null)
        {
            nextIssue = CreateIssueCore(
                state,
                new IssueRequest
                {          
                    Title = BuildPipelineFollowUpTitle(issue.Title, pipeline.RoleSequence[nextStageIndex]),
                    Detail= BuildPipelineFollowUpDetail(issue.Title, issue.Detail, pipeline.RoleSequence[nextStageIndex]),
                    RoleSlug = pipeline.RoleSequence[nextStageIndex],
                    Priority = Math.Max(1, issue.Priority - 5),
                    RoadmapItemId = issue.RoadmapItemId,
                    DependsOn =  [issue.Id],
                    Area = issue.Area,
                    FamilyKey =  issue.FamilyKey,
                    ParentIssueId = issue.Id,
                    PipelineId = pipeline.Id,
                    PipelineStageIndex = nextStageIndex
                });
            pipeline.IssueIds.Add(nextIssue.Id);
        }

        pipeline.ActiveIssueId = nextIssue.Id;
        pipeline.Status = nextIssue.Status == ItemStatus.Blocked ? PipelineStatus.Blocked : PipelineStatus.Open;
        pipeline.UpdatedAtUtc = _clock.UtcNow;
    }

    public void UpdatePipelineStatus(WorkspaceState state, IssueItem issue, PipelineStatus status, int? activeIssueId)
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
        pipeline.UpdatedAtUtc = _clock.UtcNow;
    }

    public bool HasBlockingQuestions(WorkspaceState state) =>
        state.Questions.Any(question => question.Status == QuestionStatus.Open && question.IsBlocking);

    public bool TryResolveRoleSlug(WorkspaceState state, string roleSlug, out string resolvedRoleSlug)
        => TryResolveRoleSlugInternal(state, roleSlug, out resolvedRoleSlug);

    public string ResolveRoleSlug(WorkspaceState state, string roleSlug)
    {
        TryResolveRoleSlugInternal(state, roleSlug, out var resolved);
        return resolved;
    }

    public IReadOnlyDictionary<string, string> GetKnownRoleAliases(WorkspaceState state)
    {
        return RoleAliases
            .Where(pair => state.Roles.Any(role => string.Equals(role.Slug, pair.Value, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsurePipelineForIssue(WorkspaceState state, IssueItem issue)
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
            UpdatedAtUtc = _clock.UtcNow
        };

        issue.PipelineId = pipeline.Id;
        issue.PipelineStageIndex ??= 0;
        state.Pipelines.Add(pipeline);
    }

    private static void NormalizePipelineFollowUpIssue(WorkspaceState state, IssueItem issue)
    {
        if (issue.ParentIssueId is not int parentIssueId || issue.PipelineStageIndex is null or <= 0)
        {
            return;
        }

        var parentIssue = state.Issues.FirstOrDefault(item => item.Id == parentIssueId);
        if (parentIssue is null)
        {
            return;
        }

        if (string.Equals(issue.Title, parentIssue.Title, StringComparison.Ordinal))
        {
            issue.Title = BuildPipelineFollowUpTitle(parentIssue.Title, issue.RoleSlug);
        }

        if (string.Equals(issue.Detail, parentIssue.Detail, StringComparison.Ordinal))
        {
            issue.Detail = BuildPipelineFollowUpDetail(parentIssue.Title, parentIssue.Detail, issue.RoleSlug);
        }
    }

    private static IssueItem CreateIssueCore(
        WorkspaceState state,
        IssueRequest request)
    {
        var issue = new IssueItem
        {
            Id = state.NextIssueId++,
            Title = request.Title.Trim(),
            Detail = request.Detail.Trim(),
            Area = NormalizeArea(request.Area),
            FamilyKey = NormalizeFamilyKey(request.FamilyKey, request.Title, request.Area),
            RoleSlug = ResolveRoleSlugInternal(state, request.RoleSlug),
            Priority = request.Priority,
            RoadmapItemId = request.RoadmapItemId,
            DependsOnIssueIds = request.DependsOn.Distinct().ToList(),
            ParentIssueId = request.ParentIssueId,
            PipelineId = request.PipelineId,
            PipelineStageIndex = request.PipelineStageIndex,
            ComplexityHint = request.ComplexityHint
        };
        state.Issues.Add(issue);
        return issue;
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

        // When no roles are loaded, trust the slug as-is rather than silently falling back to "developer".
        resolvedRoleSlug = state.Roles.Count == 0 ? normalized : "developer";
        return false;
    }

    private static string ResolveRoleSlugInternal(WorkspaceState state, string roleSlug)
    {
        TryResolveRoleSlugInternal(state, roleSlug, out var resolved);
        return resolved;
    }

    private static List<string> BuildPipelineSequence(WorkspaceState state, string rootRoleSlug)
    {
        var normalized = ResolveRoleSlugInternal(state, rootRoleSlug);
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

    private static int DeterminePipelineConcurrency(WorkspaceState state, IReadOnlyList<IssueItem> readyLeads, int maxSubagents)
    {
        if (readyLeads.Count == 0)
        {
            return 0;
        }

        if (state.Phase is WorkflowPhase.Planning or WorkflowPhase.ArchitectPlanning || maxSubagents <= 1)
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

    private static bool IsIssueEligibleForPhase(WorkflowPhase phase, IssueItem issue) =>
        phase switch
        {
            WorkflowPhase.Planning => issue.IsPlanningIssue,
            WorkflowPhase.ArchitectPlanning => !issue.IsPlanningIssue
                && string.Equals(issue.RoleSlug, "architect", StringComparison.OrdinalIgnoreCase),
            WorkflowPhase.Execution => !issue.IsPlanningIssue,
            _ => !issue.IsPlanningIssue
        };

    private static string BuildPipelineFollowUpTitle(string priorTitle, string nextRoleSlug)
    {
        var subject = StripLeadingStageVerb(priorTitle);
        var normalizedRole = nextRoleSlug.Trim().ToLowerInvariant();
        return normalizedRole switch
        {
            "architect" => $"Design {subject}",
            "developer" or "backend-developer" or "frontend-developer" or "fullstack-developer" => $"Implement {subject}",
            "tester" => $"Test {subject}",
            "reviewer" => $"Review {subject}",
            "ux" => $"Refine UX for {subject}",
            "user" => $"Validate {subject}",
            "game-designer" => $"Design gameplay for {subject}",
            _ => priorTitle.Trim()
        };
    }

    private static string BuildPipelineFollowUpDetail(string priorTitle, string priorDetail, string nextRoleSlug)
    {
        var subject = StripLeadingStageVerb(priorTitle);
        var normalizedRole = nextRoleSlug.Trim().ToLowerInvariant();
        return normalizedRole switch
        {
            "architect" => $"Design {subject} and define the structure, boundaries, and handoff needed for the next implementation stage.",
            "developer" or "backend-developer" or "frontend-developer" or "fullstack-developer" => $"Implement {subject} based on the approved prior-stage guidance and carry the design into working code.",
            "tester" => $"Test {subject} and verify the prior implementation is working correctly, including regressions and integration behavior.",
            "reviewer" => $"Review {subject} and capture any quality gaps, risks, or final recommendations before sign-off.",
            "ux" => $"Refine UX for {subject} and validate the interaction flow, clarity, and usability of the delivered work.",
            "user" => $"Validate {subject} from the user perspective and confirm it meets the intended outcome.",
            "game-designer" => $"Design gameplay for {subject} and define the player-facing mechanics, progression, and feel.",
            _ => priorDetail.Trim()
        };
    }

    private static string StripLeadingStageVerb(string title)
    {
        var trimmed = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "the work";
        }

        foreach (var prefix in new[]
        {
            "Design ",
            "Draft ",
            "Plan ",
            "Create ",
            "Build ",
            "Implement ",
            "Develop ",
            "Review ",
            "Verify ",
            "Validate ",
            "Test ",
            "Refine "
        })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var subject = trimmed[prefix.Length..].Trim();
                return string.IsNullOrWhiteSpace(subject) ? "the work" : subject;
            }
        }

        return trimmed;
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

    internal static string NormalizeArea(string? area)
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
