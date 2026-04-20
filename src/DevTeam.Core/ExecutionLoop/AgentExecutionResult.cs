namespace DevTeam.Core;

internal sealed record AgentExecutionResult(
    QueuedRunInfo Run,
    AgentInvocationResult Response,
    string Outcome,
    string Summary,
    string Approach,
    string Rationale,
    IReadOnlyList<GeneratedIssueProposal> Issues,
    IReadOnlyList<string> SkillsUsed,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ProposedQuestion> Questions);
