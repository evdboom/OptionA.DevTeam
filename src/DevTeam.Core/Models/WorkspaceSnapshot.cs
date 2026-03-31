namespace DevTeam.Core;

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
