namespace DevTeam.Core;

public sealed class StatusReport
{
    public Dictionary<string, int> Counts { get; init; } = new();
    public BudgetState Budget { get; init; } = new();
    public IReadOnlyList<AgentRun> QueuedRuns { get; init; } = [];
    public IReadOnlyList<QuestionItem> OpenQuestions { get; init; } = [];
    public IReadOnlyList<DecisionRecord> RecentDecisions { get; init; } = [];
}
