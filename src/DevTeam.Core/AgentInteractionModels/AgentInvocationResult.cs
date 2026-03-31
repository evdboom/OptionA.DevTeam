namespace DevTeam.Core;

public sealed class AgentInvocationResult
{
    public string BackendName { get; init; } = "";
    public string SessionId { get; init; } = "";
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
    public bool Success => ExitCode == 0;
}