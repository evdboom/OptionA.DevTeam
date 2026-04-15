namespace DevTeam.Core;

internal static class AgentClientFactory
{
    internal static IAgentClient Create(string backend, ICommandRunner? runner = null) =>
        new DefaultAgentClientFactory(runner).Create(backend);
}