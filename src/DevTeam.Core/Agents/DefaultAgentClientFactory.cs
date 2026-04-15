namespace DevTeam.Core;

public sealed class DefaultAgentClientFactory(ICommandRunner? runner = null) : IAgentClientFactory
{
    public IAgentClient Create(string backend)
    {
        return backend.Trim().ToLowerInvariant() switch
        {
            "sdk" or "copilot-sdk" => new CopilotSdkAgentClient(),
            "cli" or "copilot-cli" => new CopilotCliAgentClient(runner ?? new ProcessCommandRunner()),
            _ => throw new InvalidOperationException(
                $"Unknown agent backend '{backend}'. Expected 'cli' or 'sdk'.")
        };
    }
}
