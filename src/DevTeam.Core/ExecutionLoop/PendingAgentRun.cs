namespace DevTeam.Core;

internal sealed record PendingAgentRun(
    QueuedRunInfo Run,
    string SessionId,
    Task<AgentExecutionResult> Task,
    DateTimeOffset StartedAtUtc,
    string WorkingDirectory,
    GitStatusSnapshot? GitStatusBeforeRun);
