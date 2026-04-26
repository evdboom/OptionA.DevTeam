namespace DevTeam.Core;

public sealed class LoopExecutionOptions
{
    public int MaxIterations { get; set; } = 15;
    public int MaxSubagents { get; set; } = 4;
    public string Backend { get; set; } = "sdk";
    public string? ProviderName { get; set; }
    public TimeSpan AgentTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Timeout for planning/design roles (architect, orchestrator, backlog-manager).
    /// These roles explore the codebase and produce multi-issue plans so they need more time.
    /// Defaults to 20 minutes.
    /// </summary>
    public TimeSpan PlanningAgentTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// How much extra time to grant when an agent calls request_timeout_extension via MCP.
    /// Only one extension is allowed per run. Defaults to 10 minutes.
    /// </summary>
    public TimeSpan TimeoutExtensionAmount { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public LoopVerbosity Verbosity { get; set; } = LoopVerbosity.Normal;
    public Action<IReadOnlyList<RunProgressSnapshot>>? ProgressReporter { get; set; }

    /// <summary>
    /// Called with each streaming token fragment as the agent produces output.
    /// Receives the role slug and the raw token text so the caller can attribute output.
    /// </summary>
    public Action<string, string>? TokenReporter { get; set; }
}

public enum LoopVerbosity
{
    Quiet,
    Normal,
    Detailed
}
