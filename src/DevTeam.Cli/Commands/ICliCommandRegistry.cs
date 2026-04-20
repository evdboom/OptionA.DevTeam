namespace DevTeam.Cli;

internal interface ICliCommandRegistry
{
    void Register(ICliCommandHandler handler, params string[] commands);
}
