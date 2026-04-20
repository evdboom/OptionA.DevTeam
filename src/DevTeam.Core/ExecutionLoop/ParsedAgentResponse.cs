namespace DevTeam.Core;

public sealed record ParsedAgentResponse(
    string Outcome,
    string Summary,
    string Approach,
    string Rationale,
    IReadOnlyList<int> SelectedIssueIds,
    IReadOnlyList<GeneratedIssueProposal> Issues,
    IReadOnlyList<string> SkillsUsed,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ProposedQuestion> Questions);
