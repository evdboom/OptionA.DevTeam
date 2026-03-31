namespace DevTeam.Core;

public sealed class CommandExecutionResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = "";
    public string StdErr { get; init; } = "";
}