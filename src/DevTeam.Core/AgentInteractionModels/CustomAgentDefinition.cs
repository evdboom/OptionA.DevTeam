namespace DevTeam.Core;

/// <summary>
/// Defines a named sub-agent with an isolated tool surface and system prompt.
/// SDK-agnostic counterpart to GitHub.Copilot.SDK.CustomAgentConfig.
/// </summary>
public sealed class CustomAgentDefinition
{
    /// <summary>Machine identifier used in routing.</summary>
    public string Name { get; init; } = "";

    /// <summary>Human-readable label shown in UI.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Tells the orchestrator when to delegate to this agent.</summary>
    public string Description { get; init; } = "";

    /// <summary>Whitelist of tool names this agent may call. Enforced by the SDK.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>System prompt scoped to this agent.</summary>
    public string Prompt { get; init; } = "";

    /// <summary>When true the model may infer when to invoke this agent without an explicit handoff.</summary>
    public bool? Infer { get; init; }
}
