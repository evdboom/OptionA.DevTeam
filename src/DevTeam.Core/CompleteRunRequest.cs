namespace DevTeam.Core;

public sealed class CompleteRunRequest
{
    public int RunId { get; set; }
    public string Outcome { get; set; } = "";
    public string Summary { get; set; } = "";
    public IEnumerable<string>? SkillsUsed { get; set; }
    public IEnumerable<string>? ToolsUsed { get; set; }
    public IEnumerable<string>? ChangedPaths { get; set; }
    public IEnumerable<int>? CreatedIssueIds { get; set; }
    public IEnumerable<int>? CreatedQuestionIds { get; set; }
    public ItemStatus? ResultingIssueStatus { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? EstimatedCostUsd { get; set; }
}
