namespace DevTeam.Core;

public sealed class StatusReport
{
    public Dictionary<string, int> Counts { get; init; } = new();
    public string LoopState { get; init; } = "";
    public BudgetState Budget { get; init; } = new();
    public IReadOnlyList<AgentRun> QueuedRuns { get; init; } = [];
    public IReadOnlyList<QuestionItem> OpenQuestions { get; init; } = [];
    public IReadOnlyDictionary<int, TimeSpan> OpenQuestionAges { get; init; } = new Dictionary<int, TimeSpan>();
    public IReadOnlyList<RoleUsageSummary> RoleUsage { get; init; } = [];
    public bool IsWaitingOnBlockingQuestion { get; init; }
    public TimeSpan? OldestBlockingQuestionAge { get; init; }
    public IReadOnlyList<DecisionRecord> RecentDecisions { get; init; } = [];
}
