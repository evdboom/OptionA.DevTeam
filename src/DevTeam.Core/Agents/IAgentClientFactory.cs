namespace DevTeam.Core;

public interface IAgentClientFactory
{
    IAgentClient Create(string backend);
}
