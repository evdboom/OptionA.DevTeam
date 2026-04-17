using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevTeam.Core;

public class WorkspaceStore
{
    private const int CurrentFormatVersion = 4;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new FlexibleDateTimeOffsetJsonConverter() }
    };

    private readonly IFileSystem _fs;
    private readonly IConfigurationLoader? _configLoader;

    public WorkspaceStore(string workspacePath, IFileSystem? fileSystem = null, IConfigurationLoader? configLoader = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new ArgumentException("Workspace path must not be empty.", nameof(workspacePath));
        WorkspacePath = Path.GetFullPath(workspacePath);
        StatePath = Path.Combine(WorkspacePath, "workspace.json");
        StateDirectoryPath = Path.Combine(WorkspacePath, "state");
        _fs = fileSystem ?? new PhysicalFileSystem();
        _configLoader = configLoader;
    }

    public string WorkspacePath { get; }
    public string StatePath { get; }
    public string StateDirectoryPath { get; }

    public WorkspaceState Initialize(string repoRoot, double totalCreditCap, double premiumCreditCap)
    {
        return WithWorkspaceLock(() =>
        {
            ResetWorkspaceArtifacts();
            _fs.CreateDirectory(WorkspacePath);
            _fs.CreateDirectory(Path.Combine(WorkspacePath, "runs"));
            _fs.CreateDirectory(Path.Combine(WorkspacePath, "decisions"));
            _fs.CreateDirectory(Path.Combine(WorkspacePath, "artifacts"));
            var state = SeedData.BuildInitialState(repoRoot, totalCreditCap, premiumCreditCap, _configLoader);
            Save(state);
            return state;
        });
    }

    public WorkspaceState Load()
    {
        return WithWorkspaceLock(() =>
        {
            if (!_fs.FileExists(StatePath))
            {
                throw new InvalidOperationException(
                    $"Workspace state not found at '{StatePath}'. Run 'init' first.");
            }

            using var doc = JsonDocument.Parse(_fs.ReadAllText(StatePath));
            WorkspaceState? state;
            var migratedFromLegacy = false;
            if (doc.RootElement.TryGetProperty("FormatVersion", out var formatVersionElement)
                && formatVersionElement.GetInt32() >= CurrentFormatVersion)
            {
                var manifest = JsonSerializer.Deserialize<WorkspaceManifest>(doc.RootElement.GetRawText(), JsonOptions);
                if (manifest is null)
                {
                    throw new InvalidOperationException("Failed to deserialize workspace manifest.");
                }

                state = LoadFromManifest(manifest);
            }
            else
            {
                state = JsonSerializer.Deserialize<WorkspaceState>(doc.RootElement.GetRawText(), JsonOptions);
                migratedFromLegacy = true;
            }

            if (state is null)
            {
                throw new InvalidOperationException("Failed to deserialize workspace state.");
            }

            var hydratedMetadata = SeedData.HydrateMissingWorkspaceMetadata(state, _configLoader);
            if (migratedFromLegacy || hydratedMetadata)
            {
                Save(state);
            }

            return state;
        });
    }

    public void Save(WorkspaceState state)
    {
        WithWorkspaceLock(() =>
        {
            _fs.CreateDirectory(WorkspacePath);
            _fs.CreateDirectory(Path.Combine(WorkspacePath, "runs"));
            _fs.CreateDirectory(Path.Combine(WorkspacePath, "decisions"));
            _fs.CreateDirectory(Path.Combine(WorkspacePath, "artifacts"));
            _fs.CreateDirectory(StateDirectoryPath);
            SaveCollections(state);
            AtomicWriteAllText(StatePath, JsonSerializer.Serialize(CreateManifest(state), JsonOptions));
            WriteIssueBoard(state);
            WriteQuestionsFile(state);
            WriteCodebaseContextFile(state);
            return 0;
        });
    }

    private void ResetWorkspaceArtifacts()
    {
        if (!_fs.DirectoryExists(WorkspacePath))
        {
            return;
        }

        foreach (var directory in new[] { "runs", "decisions", "artifacts", "issues", "state" })
        {
            var path = Path.Combine(WorkspacePath, directory);
            if (_fs.DirectoryExists(path))
            {
                _fs.DeleteDirectory(path, true);
            }
        }

        foreach (var file in new[] { "workspace.json", "questions.md", "plan.md" })
        {
            var path = Path.Combine(WorkspacePath, file);
            if (_fs.FileExists(path))
            {
                _fs.DeleteFile(path);
            }
        }
    }

    private WorkspaceState LoadFromManifest(WorkspaceManifest manifest)
    {
        return new WorkspaceState
        {
            RepoRoot = manifest.RepoRoot,
            Phase = manifest.Phase,
            Budget = manifest.Budget,
            Runtime = manifest.Runtime ?? RuntimeConfiguration.CreateDefault(),
            ActiveGoal = manifest.ActiveGoal,
            Roadmap = ReadCollection<RoadmapItem>(manifest.RoadmapFile),
            Issues = ReadCollection<IssueItem>(manifest.IssuesFile),
            Questions = ReadCollection<QuestionItem>(manifest.QuestionsFile),
            AgentRuns = ReadCollection<AgentRun>(manifest.RunsFile),
            AgentSessions = ReadCollection<AgentSession>(manifest.AgentSessionsFile),
            ExecutionSelection = manifest.ExecutionSelection ?? new ExecutionSelectionState(),
            Decisions = ReadCollection<DecisionRecord>(manifest.DecisionsFile),
            Pipelines = ReadCollection<PipelineState>(manifest.PipelinesFile),
            NextRoadmapId = manifest.NextRoadmapId,
            NextIssueId = manifest.NextIssueId,
            NextQuestionId = manifest.NextQuestionId,
            NextRunId = manifest.NextRunId,
            NextDecisionId = manifest.NextDecisionId,
            NextPipelineId = manifest.NextPipelineId
        };
    }

    private WorkspaceManifest CreateManifest(WorkspaceState state)
    {
        return new WorkspaceManifest
        {
            FormatVersion = CurrentFormatVersion,
            RepoRoot = state.RepoRoot,
            Phase = state.Phase,
            Budget = state.Budget,
            Runtime = state.Runtime,
            ActiveGoal = state.ActiveGoal,
            ExecutionSelection = state.ExecutionSelection,
            RoadmapFile = "roadmap.json",
            IssuesFile = "issues.json",
            QuestionsFile = "questions.json",
            RunsFile = "runs.json",
            AgentSessionsFile = "sessions.json",
            DecisionsFile = "decisions.json",
            PipelinesFile = "pipelines.json",
            RoadmapCount = state.Roadmap.Count,
            IssueCount = state.Issues.Count,
            QuestionCount = state.Questions.Count,
            RunCount = state.AgentRuns.Count,
            SessionCount = state.AgentSessions.Count,
            DecisionCount = state.Decisions.Count,
            PipelineCount = state.Pipelines.Count,
            NextRoadmapId = state.NextRoadmapId,
            NextIssueId = state.NextIssueId,
            NextQuestionId = state.NextQuestionId,
            NextRunId = state.NextRunId,
            NextDecisionId = state.NextDecisionId,
            NextPipelineId = state.NextPipelineId
        };
    }

    private void SaveCollections(WorkspaceState state)
    {
        WriteCollection("roadmap.json", state.Roadmap);
        WriteCollection("issues.json", state.Issues);
        WriteCollection("questions.json", state.Questions);
        WriteCollection("runs.json", state.AgentRuns);
        WriteCollection("sessions.json", state.AgentSessions);
        WriteCollection("decisions.json", state.Decisions);
        WriteCollection("pipelines.json", state.Pipelines);
        DeleteCollection("models.json");
        DeleteCollection("providers.json");
        DeleteCollection("roles.json");
        DeleteCollection("superpowers.json");
    }

    private List<T> ReadCollection<T>(string fileName)
    {
        var path = Path.Combine(StateDirectoryPath, fileName);
        if (!_fs.FileExists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<T>>(_fs.ReadAllText(path), JsonOptions) ?? [];
    }

    private void WriteCollection<T>(string fileName, IReadOnlyList<T> items)
    {
        var path = Path.Combine(StateDirectoryPath, fileName);
        AtomicWriteAllText(path, JsonSerializer.Serialize(items, JsonOptions));
    }

    private void DeleteCollection(string fileName)
    {
        var path = Path.Combine(StateDirectoryPath, fileName);
        if (_fs.FileExists(path))
        {
            _fs.DeleteFile(path);
        }
    }

    private void WriteQuestionsFile(WorkspaceState state)
    {
        var openQuestions = state.Questions
            .Where(item => item.Status == QuestionStatus.Open)
            .OrderBy(item => item.Id)
            .ToList();
        var path = Path.Combine(WorkspacePath, "questions.md");

        if (openQuestions.Count == 0)
        {
            AtomicWriteAllText(path, "# Open questions\n\n(none)\n");
            return;
        }

        var lines = new List<string>
        {
            "# Open questions",
            ""
        };

        foreach (var question in openQuestions)
        {
            lines.Add($"## Question {question.Id}");
            lines.Add("");
            lines.Add($"- Type: {(question.IsBlocking ? "blocking" : "non-blocking")}");
            lines.Add($"- Status: {question.Status}");
            if (!string.IsNullOrWhiteSpace(question.ExternalReference))
            {
                lines.Add($"- External: {question.ExternalReference}");
            }
            if (question.CreatedAtUtc != default)
            {
                lines.Add($"- Asked: {question.CreatedAtUtc:O}");
            }
            lines.Add("");
            lines.Add(question.Text);
            lines.Add("");
        }

        AtomicWriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private void WriteCodebaseContextFile(WorkspaceState state)
    {
        if (string.IsNullOrWhiteSpace(state.CodebaseContext)) return;
        var path = Path.Combine(WorkspacePath, "codebase-context.md");
        AtomicWriteAllText(path, state.CodebaseContext.Trim() + Environment.NewLine);
    }

    private void WriteIssueBoard(WorkspaceState state)
    {
        var issuesDir = Path.Combine(WorkspacePath, "issues");
        _fs.CreateDirectory(issuesDir);

        var indexLines = new List<string>
        {
            "# Issue Index",
            "",
            "| ID   | Title | Status | Role | Area | Depends On |",
            "|------|-------|--------|------|------|------------|"
        };

        foreach (var issue in state.Issues.OrderBy(item => item.Id))
        {
            var dependsOn = issue.DependsOnIssueIds.Count == 0
                ? "—"
                : string.Join(", ", issue.DependsOnIssueIds.Select(FormatIssueId));
            indexLines.Add(
                $"| {FormatIssueId(issue.Id)} | {EscapePipe(issue.Title)} | {issue.Status.ToString().ToLowerInvariant()} | {issue.RoleSlug} | {(string.IsNullOrWhiteSpace(issue.Area) ? "—" : issue.Area)} | {dependsOn} |");

            WriteIssueFile(issuesDir, state, issue);
        }

        AtomicWriteAllText(
            Path.Combine(issuesDir, "_index.md"),
            string.Join(Environment.NewLine, indexLines) + Environment.NewLine);
    }

    private void WriteIssueFile(string issuesDir, WorkspaceState state, IssueItem issue)
    {
        var latestRun = state.AgentRuns
            .Where(run => run.IssueId == issue.Id)
            .OrderByDescending(run => run.UpdatedAtUtc)
            .FirstOrDefault();
        var relatedDecisions = state.Decisions
            .Where(item => item.IssueId == issue.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(5)
            .ToList();

        var path = Path.Combine(issuesDir, $"{FormatIssueId(issue.Id)}-{Slugify(issue.Title)}.md");
        var dependsOn = issue.DependsOnIssueIds.Count == 0
            ? "none"
            : string.Join(", ", issue.DependsOnIssueIds.Select(FormatIssueId));
        var roadmap = issue.RoadmapItemId is null ? "none" : issue.RoadmapItemId.Value.ToString();
        var pipeline = issue.PipelineId is null ? "none" : issue.PipelineId.Value.ToString();
        var externalReference = string.IsNullOrWhiteSpace(issue.ExternalReference) ? "none" : issue.ExternalReference;
        var latestRunBlock = latestRun is null
            ? "(none)"
            : $"""
            - Run: {latestRun.Id}
            - Status: {latestRun.Status}
            - Model: {latestRun.ModelName}
            - Session: {latestRun.SessionId}
            - Updated: {latestRun.UpdatedAtUtc:O}
            - Summary: {latestRun.Summary}
            - Superpowers Used: {(latestRun.SuperpowersUsed.Count == 0 ? "none" : string.Join(", ", latestRun.SuperpowersUsed))}
            - Tools Used: {(latestRun.ToolsUsed.Count == 0 ? "none" : string.Join(", ", latestRun.ToolsUsed))}
            - Changed Files: {(latestRun.ChangedPaths.Count == 0 ? "none" : string.Join(", ", latestRun.ChangedPaths))}
            """;
        var decisionBlock = relatedDecisions.Count == 0
            ? "(none)"
            : string.Join(
                Environment.NewLine,
                relatedDecisions.Select(item => $"- #{item.Id} [{item.Source}] {item.Title}: {item.Detail}"));

        var content = $"""
        # Issue {FormatIssueId(issue.Id)}: {issue.Title}

        - Status: {issue.Status.ToString().ToLowerInvariant()}
        - Role: {issue.RoleSlug}
        - Area: {(string.IsNullOrWhiteSpace(issue.Area) ? "none" : issue.Area)}
        - Priority: {issue.Priority}
        - Depends On: {dependsOn}
        - Roadmap Item: {roadmap}
        - Family: {(string.IsNullOrWhiteSpace(issue.FamilyKey) ? "none" : issue.FamilyKey)}
        - External: {externalReference}
        - Pipeline: {pipeline}
        - Pipeline Stage: {(issue.PipelineStageIndex?.ToString() ?? "none")}
        - Planning Issue: {(issue.IsPlanningIssue ? "yes" : "no")}

        ## Detail

        {issue.Detail}

        ## Latest Run

        {latestRunBlock}

        ## Recent Decisions

        {decisionBlock}
        """;

        AtomicWriteAllText(path, content);
    }

    private T WithWorkspaceLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, BuildWorkspaceMutexName());
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new IOException($"Timed out waiting for exclusive access to workspace '{WorkspacePath}'.");
            }

            return action();
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private string BuildWorkspaceMutexName()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(WorkspacePath.ToLowerInvariant()));
        var hash = Convert.ToHexString(bytes[..8]);
        return $"OptionA.DevTeam.Workspace.{hash}";
    }

    private void AtomicWriteAllText(string path, string content)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            _fs.WriteAllText(tempPath, content);
            if (_fs.FileExists(path))
            {
                ReplaceWithFallback(tempPath, path, content);
                tempPath = string.Empty;
                return;
            }

            _fs.MoveFile(tempPath, path);
            tempPath = string.Empty;
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && _fs.FileExists(tempPath))
            {
                _fs.DeleteFile(tempPath);
            }
        }
    }

    private void ReplaceWithFallback(string tempPath, string path, string content)
    {
        Exception? replaceFailure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                _fs.ReplaceFile(tempPath, path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                replaceFailure = ex;
                System.Threading.Thread.Sleep(50 * (attempt + 1));
            }
        }

        try
        {
            _fs.WriteAllText(path, content);
            _fs.DeleteFile(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Failed to replace '{path}' after repeated retries and in-place fallback.",
                new AggregateException(replaceFailure ?? ex, ex));
        }
    }

    private static string FormatIssueId(int id) => id.ToString("0000");

    private static string EscapePipe(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Slugify(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }

    private sealed class WorkspaceManifest
    {
        public int FormatVersion { get; init; }
        public string RepoRoot { get; init; } = "";
        public WorkflowPhase Phase { get; init; } = WorkflowPhase.Planning;
        public BudgetState Budget { get; init; } = new();
        public RuntimeConfiguration? Runtime { get; init; }
        public GoalState? ActiveGoal { get; init; }
        public ExecutionSelectionState? ExecutionSelection { get; init; }
        public string RoadmapFile { get; init; } = "roadmap.json";
        public string IssuesFile { get; init; } = "issues.json";
        public string QuestionsFile { get; init; } = "questions.json";
        public string RunsFile { get; init; } = "runs.json";
        public string AgentSessionsFile { get; init; } = "sessions.json";
        public string DecisionsFile { get; init; } = "decisions.json";
        public string PipelinesFile { get; init; } = "pipelines.json";
        public int RoadmapCount { get; init; }
        public int IssueCount { get; init; }
        public int QuestionCount { get; init; }
        public int RunCount { get; init; }
        public int SessionCount { get; init; }
        public int DecisionCount { get; init; }
        public int PipelineCount { get; init; }
        public int ModelCount { get; init; }
        public int RoleCount { get; init; }
        public int SuperpowerCount { get; init; }
        public int NextRoadmapId { get; init; } = 1;
        public int NextIssueId { get; init; } = 1;
        public int NextQuestionId { get; init; } = 1;
        public int NextRunId { get; init; } = 1;
        public int NextDecisionId { get; init; } = 1;
        public int NextPipelineId { get; init; } = 1;
    }

    private sealed class FlexibleDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
    {
        private static readonly string[] LegacyFormats =
        [
            "dd/MM/yyyy HH:mm:ss",
            "d/M/yyyy HH:mm:ss",
            "MM/dd/yyyy HH:mm:ss",
            "M/d/yyyy HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss"
        ];

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => ParseString(reader.GetString() ?? string.Empty),
                JsonTokenType.Number => ParseNumber(ref reader),
                _ => throw new JsonException($"Unexpected token {reader.TokenType} for DateTimeOffset.")
            };
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));

        private static DateTimeOffset ParseString(string value)
        {
            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var parsed))
            {
                return parsed;
            }

            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out parsed))
            {
                return parsed;
            }

            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en-GB"), CultureInfo.GetCultureInfo("nl-NL") })
            {
                if (DateTime.TryParseExact(
                    value,
                    LegacyFormats,
                    culture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var localDateTime))
                {
                    return new DateTimeOffset(localDateTime);
                }
            }

            throw new JsonException($"The JSON value '{value}' could not be converted to {nameof(DateTimeOffset)}.");
        }

        private static DateTimeOffset ParseNumber(ref Utf8JsonReader reader)
        {
            var numeric = reader.GetInt64();
            return numeric > 9_999_999_999
                ? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
                : DateTimeOffset.FromUnixTimeSeconds(numeric);
        }
    }
}

