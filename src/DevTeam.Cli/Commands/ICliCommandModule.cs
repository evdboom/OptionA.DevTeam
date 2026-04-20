namespace DevTeam.Cli;

internal interface ICliCommandModule
{
    void Register(ICliCommandRegistry registry);
}
