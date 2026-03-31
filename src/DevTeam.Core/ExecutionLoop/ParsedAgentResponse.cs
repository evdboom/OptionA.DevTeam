namespace DevTeam.Core;

public sealed record ParsedAgentResponse(
    string Outcome,
    string Summary,
    IReadOnlyList<int> SelectedIssueIds,
    IReadOnlyList<GeneratedIssueProposal> Issues,
    IReadOnlyList<string> SuperpowersUsed,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ProposedQuestion> Questions);
