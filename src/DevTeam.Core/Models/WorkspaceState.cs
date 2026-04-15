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
    public List<WorktreeEntry> Worktrees { get; set; } = [];
    public int NextRoadmapId { get; set; } = 1;
    public int NextIssueId { get; set; } = 1;
    public int NextQuestionId { get; set; } = 1;
    public int NextRunId { get; set; } = 1;
    public int NextDecisionId { get; set; } = 1;
    public int NextPipelineId { get; set; } = 1;
}