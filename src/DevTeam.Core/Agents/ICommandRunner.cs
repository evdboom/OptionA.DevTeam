namespace DevTeam.Core;

public interface ICommandRunner
{
    Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default);
}