using System.Text.Json.Serialization;

namespace DevTeam.Core;

public sealed class WorkspaceState
{
    public string RepoRoot { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowPhase Phase { get; set; } = WorkflowPhase.Planning;
    public BudgetState Budget { get; set; } = new();
    public RuntimeConfiguration Runtime { get; set; } = RuntimeConfiguration.CreateDefault();
    public GoalState? ActiveGoal { get; set; }
    public List<RoadmapItem> Roadmap { get; set; } = [];
    public List<IssueItem> Issues { get; set; } = [];
    public List<QuestionItem> Questions { get; set; } = [];
    public List<AgentRun> AgentRuns { get; set; } = [];
    public List<AgentSession> AgentSessions { get; set; } = [];
    public ExecutionSelectionState ExecutionSelection { get; set; } = new();
    public List<DecisionRecord> Decisions { get; set; } = [];
    public List<PipelineState> Pipelines { get; set; } = [];
    public List<ModelDefinition> Models { get; set; } = [];
    public List<ModeDefinition> Modes { get; set; } = [];
    public List<RoleDefinition> Roles { get; set; } = [];
    public List<SuperpowerDefinition> Superpowers { get; set; } = [];
    public List<McpServerDefinition> McpServers { get; set; } = [];
    public int NextRoadmapId { get; set; } = 1;
    public int NextIssueId { get; set; } = 1;
    public int NextQuestionId { get; set; } = 1;
    public int NextRunId { get; set; } = 1;
    public int NextDecisionId { get; set; } = 1;
    public int NextPipelineId { get; set; } = 1;
}

public sealed class RuntimeConfiguration
{
    public string ActiveModeSlug { get; set; } = "develop";
    public bool KeepAwakeEnabled { get; set; }
    public bool WorkspaceMcpEnabled { get; set; } = true;
    public bool PipelineSchedulingEnabled { get; set; } = true;
    public bool AutoApproveEnabled { get; set; }
    public string WorkspaceMcpServerName { get; set; } = "devteam-workspace";
    public List<string> DefaultPipelineRoles { get; set; } = ["architect", "developer", "tester"];
    public int DefaultMaxIterations { get; set; } = 15;
    public int DefaultMaxSubagents { get; set; } = 4;

    public static RuntimeConfiguration CreateDefault() => new();
}

public sealed class BudgetState
{
    public double TotalCreditCap { get; set; } = 25;
    public double PremiumCreditCap { get; set; } = 6;
    public double CreditsCommitted { get; set; }
    public double PremiumCreditsCommitted { get; set; }
}

public sealed class GoalState
{
    public string GoalText { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RoadmapItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemStatus Status { get; set; } = ItemStatus.Open;
    public int Priority { get; set; } = 50;
}

public sealed class IssueItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Area { get; set; } = "";
    public string FamilyKey { get; set; } = "";
    public bool IsPlanningIssue { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemStatus Status { get; set; } = ItemStatus.Open;
    public string RoleSlug { get; set; } = "developer";
    public int Priority { get; set; } = 50;
    public int? RoadmapItemId { get; set; }
    public List<int> DependsOnIssueIds { get; set; } = [];
    public int? ParentIssueId { get; set; }
    public int? PipelineId { get; set; }
    public int? PipelineStageIndex { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class QuestionItem
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool IsBlocking { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public QuestionStatus Status { get; set; } = QuestionStatus.Open;
    public string Answer { get; set; } = "";
}

public sealed class AgentRun
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public string RoleSlug { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string SessionId { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Queued;
    public string Summary { get; set; } = "";
    public List<string> SuperpowersUsed { get; set; } = [];
    public List<string> ToolsUsed { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentSession
{
    public string ScopeKey { get; set; } = "";
    public string ScopeKind { get; set; } = "";
    public string RoleSlug { get; set; } = "";
    public int? IssueId { get; set; }
    public int? PipelineId { get; set; }
    public int? LastRunId { get; set; }
    public string SessionId { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExecutionSelectionState
{
    public List<int> SelectedIssueIds { get; set; } = [];
    public string Rationale { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DecisionRecord
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Source { get; set; } = "";
    public int? IssueId { get; set; }
    public int? RunId { get; set; }
    public string SessionId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PipelineState
{
    public int Id { get; set; }
    public int RootIssueId { get; set; }
    public string FamilyKey { get; set; } = "";
    public string Area { get; set; } = "";
    public List<string> RoleSequence { get; set; } = [];
    public List<int> IssueIds { get; set; } = [];
    public int? ActiveIssueId { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PipelineStatus Status { get; set; } = PipelineStatus.Open;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ModelDefinition
{
    public string Name { get; set; } = "";
    public double Cost { get; set; }
    public bool IsDefault { get; set; }
    public bool IsPremium { get; set; }
}

public sealed class RoleDefinition
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string SuggestedModel { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> RequiredTools { get; set; } = [];
}

public sealed class ModeDefinition
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class SuperpowerDefinition
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Body { get; set; } = "";
    public List<string> RequiredTools { get; set; } = [];
}

public sealed class McpServerDefinition
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public string? Cwd { get; set; }
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class RoleModelPolicy
{
    public string PrimaryModel { get; set; } = "";
    public string FallbackModel { get; set; } = "";
    public bool AllowPremium { get; set; }
}

public sealed class QueuedRunInfo
{
    public int RunId { get; init; }
    public int IssueId { get; init; }
    public string Title { get; init; } = "";
    public string RoleSlug { get; init; } = "";
    public string Area { get; init; } = "";
    public string ModelName { get; init; } = "";
}

public sealed class LoopResult
{
    public string State { get; init; } = "idle";
    public IReadOnlyList<string> Created { get; init; } = [];
    public IReadOnlyList<QueuedRunInfo> QueuedRuns { get; init; } = [];
}

public sealed class WorkspaceSnapshot
{
    public string RepoRoot { get; init; } = "";
    public WorkflowPhase Phase { get; init; }
    public GoalState? ActiveGoal { get; init; }
    public RuntimeConfiguration Runtime { get; init; } = RuntimeConfiguration.CreateDefault();
    public IReadOnlyList<ModeDefinition> Modes { get; init; } = [];
    public IReadOnlyList<IssueItem> Issues { get; init; } = [];
    public IReadOnlyList<QuestionItem> Questions { get; init; } = [];
    public IReadOnlyList<AgentSession> AgentSessions { get; init; } = [];
    public ExecutionSelectionState ExecutionSelection { get; init; } = new();
    public IReadOnlyList<DecisionRecord> Decisions { get; init; } = [];
    public IReadOnlyList<PipelineState> Pipelines { get; init; } = [];
}

public sealed class StatusReport
{
    public Dictionary<string, int> Counts { get; init; } = new();
    public BudgetState Budget { get; init; } = new();
    public IReadOnlyList<AgentRun> QueuedRuns { get; init; } = [];
    public IReadOnlyList<QuestionItem> OpenQuestions { get; init; } = [];
    public IReadOnlyList<DecisionRecord> RecentDecisions { get; init; } = [];
}

public sealed class ProposedQuestion
{
    public string Text { get; init; } = "";
    public bool IsBlocking { get; init; }
}

public sealed class GeneratedIssueProposal
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string RoleSlug { get; init; } = "developer";
    public string Area { get; init; } = "";
    public int Priority { get; init; } = 50;
    public IReadOnlyList<int> DependsOnIssueIds { get; init; } = [];
}

public enum ItemStatus
{
    Open,
    InProgress,
    Done,
    Blocked
}

public enum QuestionStatus
{
    Open,
    Answered
}

public enum AgentRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Blocked
}

public enum PipelineStatus
{
    Open,
    Running,
    Completed,
    Blocked
}

public enum WorkflowPhase
{
    Planning,
    ArchitectPlanning,
    Execution
}

