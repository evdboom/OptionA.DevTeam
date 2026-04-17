namespace DevTeam.Core;

public sealed class RunDiffReport
{
    public required AgentRun PrimaryRun { get; init; }
    public IssueItem? PrimaryIssue { get; init; }
    public IReadOnlyList<IssueItem> PrimaryCreatedIssues { get; init; } = [];
    public IReadOnlyList<QuestionItem> PrimaryCreatedQuestions { get; init; } = [];
    public AgentRun? CompareRun { get; init; }
    public IssueItem? CompareIssue { get; init; }
    public IReadOnlyList<IssueItem> CompareCreatedIssues { get; init; } = [];
    public IReadOnlyList<QuestionItem> CompareCreatedQuestions { get; init; } = [];
    public IReadOnlyList<string> SharedChangedPaths { get; init; } = [];
    public IReadOnlyList<string> PrimaryOnlyChangedPaths { get; init; } = [];
    public IReadOnlyList<string> CompareOnlyChangedPaths { get; init; } = [];
}
