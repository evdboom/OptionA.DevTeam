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
}
