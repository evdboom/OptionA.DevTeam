namespace DevTeam.Core;

public static class AgentClientFactory
{
    public static IAgentClient Create(string backend, ICommandRunner? runner = null)
    {
        return backend.Trim().ToLowerInvariant() switch
        {
            "sdk" or "copilot-sdk" => new CopilotSdkAgentClient(),
            "cli" or "copilot-cli" => new CopilotCliAgentClient(runner),
            _ => throw new InvalidOperationException(
                $"Unknown agent backend '{backend}'. Expected 'cli' or 'sdk'.")
        };
    }
}