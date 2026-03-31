namespace DevTeam.Core;

public sealed class LoopExecutionReport
{
    public int IterationsExecuted { get; init; }
    public string FinalState { get; init; } = "idle";
}
