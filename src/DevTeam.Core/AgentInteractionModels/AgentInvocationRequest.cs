namespace DevTeam.Core;

public sealed class AgentInvocationRequest
{
    public string Prompt { get; init; } = "";
    public string? Model { get; init; }
    public string? SessionId { get; init; }
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string? WorkspacePath { get; init; }
    public ProviderDefinition? Provider { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(20);
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];
    public bool EnableWorkspaceMcp { get; init; }
    public string WorkspaceMcpServerName { get; init; } = "devteam-workspace";
    public string? ToolHostPath { get; init; }
    public IReadOnlyList<McpServerDefinition> ExternalMcpServers { get; init; } = [];

    /// <summary>
    /// Called on each streaming token as the agent produces output.
    /// The callback receives raw text fragments; may be called from any thread.
    /// </summary>
    public Action<string>? OnToken { get; init; }

    /// <summary>
    /// Optional lifecycle and tool-interception hooks. Only used by the Copilot SDK backend.
    /// </summary>
    public SessionHooksConfig? Hooks { get; init; }

    /// <summary>
    /// Named sub-agents with isolated tool surfaces. Only used by the Copilot SDK backend.
    /// </summary>
    public IReadOnlyList<CustomAgentDefinition> CustomAgents { get; init; } = [];
}
