namespace DevTeam.Cli;

internal interface ICliCommandHandler
{
    Task<int> ExecuteAsync(Dictionary<string, List<string>> options);
}
