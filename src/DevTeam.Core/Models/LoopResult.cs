namespace DevTeam.Core;

public sealed class LoopResult
{
    public string State { get; init; } = "idle";
    public IReadOnlyList<string> Created { get; init; } = [];
    public IReadOnlyList<QueuedRunInfo> QueuedRuns { get; init; } = [];
}
