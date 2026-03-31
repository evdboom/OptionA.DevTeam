namespace DevTeam.Core;

internal sealed record AgentExecutionResult(
    QueuedRunInfo Run,
    AgentInvocationResult Response,
    string Outcome,
    string Summary,
    IReadOnlyList<GeneratedIssueProposal> Issues,
    IReadOnlyList<string> SuperpowersUsed,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<ProposedQuestion> Questions);
