namespace DevTeam.Core;

public interface IAgentClient
{
    string Name { get; }
    Task<AgentInvocationResult> InvokeAsync(AgentInvocationRequest request, CancellationToken cancellationToken = default);
}