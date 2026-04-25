using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class RepoMemoryStore(string repoRoot, IFileSystem fileSystem)
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _repoRoot = Path.GetFullPath(repoRoot);
    private readonly IFileSystem _fileSystem = fileSystem;

    public string DirectoryPath => Path.Combine(_repoRoot, CoreConstants.Paths.DevTeamRepo);
    public string ManifestPath => Path.Combine(DirectoryPath, "manifest.json");
    public string GoalPath => Path.Combine(DirectoryPath, "GOAL.md");
    public string StatusPath => Path.Combine(DirectoryPath, "STATUS.md");
    public string DecisionsPath => Path.Combine(DirectoryPath, "DECISIONS.md");
    public string ContextPath => Path.Combine(DirectoryPath, "CONTEXT.md");
    public string PlanPath => Path.Combine(DirectoryPath, "PLAN.md");

    public void Save(WorkspaceState state, string workspacePath)
    {
        _fileSystem.CreateDirectory(DirectoryPath);

        var durableDecisions = state.Decisions
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(20)
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new RepoMemoryDecision
            {
                Title = item.Title,
                Detail = item.Detail,
                Source = item.Source,
                CreatedAtUtc = item.CreatedAtUtc
            })
            .ToList();

        var manifest = new RepoMemoryManifest
        {
            FormatVersion = CurrentFormatVersion,
            GoalText = state.ActiveGoal?.GoalText ?? string.Empty,
            Phase = state.Phase,
            ActiveModeSlug = state.Runtime.ActiveModeSlug,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            OpenIssueCount = state.Issues.Count(item => item.Status != ItemStatus.Done),
            OpenQuestionCount = state.Questions.Count(item => item.Status == QuestionStatus.Open),
            DurableDecisions = durableDecisions
        };

        _fileSystem.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        _fileSystem.WriteAllText(GoalPath, BuildGoalMarkdown(state));
        _fileSystem.WriteAllText(StatusPath, BuildStatusMarkdown(state));
        _fileSystem.WriteAllText(DecisionsPath, BuildDecisionsMarkdown(durableDecisions));

        if (string.IsNullOrWhiteSpace(state.CodebaseContext))
        {
            DeleteIfExists(ContextPath);
        }
        else
        {
            _fileSystem.WriteAllText(ContextPath, state.CodebaseContext.Trim() + Environment.NewLine);
        }

        var workspacePlanPath = Path.Combine(workspacePath, "plan.md");
        if (_fileSystem.FileExists(workspacePlanPath))
        {
            _fileSystem.WriteAllText(PlanPath, _fileSystem.ReadAllText(workspacePlanPath));
        }
        else
        {
            DeleteIfExists(PlanPath);
        }
    }

    public bool TryHydrate(WorkspaceState state)
    {
        if (!_fileSystem.FileExists(ManifestPath))
        {
            return false;
        }

        var manifest = JsonSerializer.Deserialize<RepoMemoryManifest>(_fileSystem.ReadAllText(ManifestPath), JsonOptions);
        if (manifest is null || manifest.FormatVersion < CurrentFormatVersion)
        {
            return false;
        }

        if (state.ActiveGoal is null && !string.IsNullOrWhiteSpace(manifest.GoalText))
        {
            state.ActiveGoal = new GoalState
            {
                GoalText = manifest.GoalText.Trim(),
                UpdatedAtUtc = manifest.GeneratedAtUtc == default ? DateTimeOffset.UtcNow : manifest.GeneratedAtUtc
            };
        }

        state.Phase = manifest.Phase;

        if (string.IsNullOrWhiteSpace(state.Runtime.ActiveModeSlug) && !string.IsNullOrWhiteSpace(manifest.ActiveModeSlug))
        {
            state.Runtime.ActiveModeSlug = manifest.ActiveModeSlug;
        }

        if (string.IsNullOrWhiteSpace(state.CodebaseContext) && _fileSystem.FileExists(ContextPath))
        {
            state.CodebaseContext = _fileSystem.ReadAllText(ContextPath).Trim();
        }

        if (state.Decisions.Count == 0)
        {
            foreach (var decision in manifest.DurableDecisions)
            {
                state.Decisions.Add(new DecisionRecord
                {
                    Id = state.NextDecisionId++,
                    Title = decision.Title,
                    Detail = decision.Detail,
                    Source = decision.Source,
                    CreatedAtUtc = decision.CreatedAtUtc == default ? DateTimeOffset.UtcNow : decision.CreatedAtUtc
                });
            }
        }

        return true;
    }

    private void DeleteIfExists(string path)
    {
        if (_fileSystem.FileExists(path))
        {
            _fileSystem.DeleteFile(path);
        }
    }

    private static string BuildGoalMarkdown(WorkspaceState state)
    {
        if (state.ActiveGoal is null || string.IsNullOrWhiteSpace(state.ActiveGoal.GoalText))
        {
            return "# Goal\n\n(none)\n";
        }

        return $"# Goal{Environment.NewLine}{Environment.NewLine}{state.ActiveGoal.GoalText.Trim()}{Environment.NewLine}";
    }

    private static string BuildStatusMarkdown(WorkspaceState state)
    {
        var openIssues = state.Issues
            .Where(item => item.Status != ItemStatus.Done)
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Id)
            .Take(10)
            .ToList();
        var openQuestions = state.Questions
            .Where(item => item.Status == QuestionStatus.Open)
            .OrderBy(item => item.Id)
            .ToList();
        var issueLines = openIssues.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, openIssues.Select(item =>
                $"- #{item.Id} [{item.RoleSlug}{(string.IsNullOrWhiteSpace(item.Area) ? string.Empty : $" @ {item.Area}")}] {item.Title}"));
        var questionLines = openQuestions.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, openQuestions.Select(item =>
                $"- #{item.Id} [{(item.IsBlocking ? "blocking" : "non-blocking")}] {item.Text}"));

        return $"""
        # Repo Status

        - Phase: {state.Phase}
        - Mode: {state.Runtime.ActiveModeSlug}
        - Goal: {(string.IsNullOrWhiteSpace(state.ActiveGoal?.GoalText) ? "(none)" : state.ActiveGoal!.GoalText.Trim())}
        - Open Issues: {state.Issues.Count(item => item.Status != ItemStatus.Done)}
        - Open Questions: {openQuestions.Count}

        ## Active Work

        {issueLines}

        ## Open Questions

        {questionLines}
        """ + Environment.NewLine;
    }

    private static string BuildDecisionsMarkdown(IReadOnlyList<RepoMemoryDecision> decisions)
    {
        if (decisions.Count == 0)
        {
            return "# Durable Decisions\n\n(none)\n";
        }

        var lines = new List<string>
        {
            "# Durable Decisions",
            string.Empty
        };

        foreach (var decision in decisions)
        {
            lines.Add($"## {decision.Title}");
            lines.Add(string.Empty);
            lines.Add($"- Source: {decision.Source}");
            if (decision.CreatedAtUtc != default)
            {
                lines.Add($"- Recorded: {decision.CreatedAtUtc:O}");
            }
            lines.Add(string.Empty);
            lines.Add(decision.Detail);
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private sealed class RepoMemoryManifest
    {
        public int FormatVersion { get; set; }
        public string GoalText { get; set; } = string.Empty;
        public WorkflowPhase Phase { get; set; }
        public string ActiveModeSlug { get; set; } = string.Empty;
        public DateTimeOffset GeneratedAtUtc { get; set; }
        public int OpenIssueCount { get; set; }
        public int OpenQuestionCount { get; set; }
        public List<RepoMemoryDecision> DurableDecisions { get; set; } = [];
    }

    private sealed class RepoMemoryDecision
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}