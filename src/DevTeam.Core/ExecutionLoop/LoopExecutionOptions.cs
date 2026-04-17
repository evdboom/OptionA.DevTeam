namespace DevTeam.Core;

public sealed class LoopExecutionOptions
{
    public int MaxIterations { get; set; } = 15;
    public int MaxSubagents { get; set; } = 4;
    public string Backend { get; set; } = "sdk";
    public string? ProviderName { get; set; }
    public TimeSpan AgentTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public LoopVerbosity Verbosity { get; set; } = LoopVerbosity.Normal;
    public Action<IReadOnlyList<RunProgressSnapshot>>? ProgressReporter { get; set; }
}

public enum LoopVerbosity
{
    Quiet,
    Normal,
    Detailed
}
