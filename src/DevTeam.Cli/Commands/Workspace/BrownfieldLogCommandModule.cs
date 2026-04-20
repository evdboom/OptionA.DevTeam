namespace DevTeam.Cli;

internal sealed class BrownfieldLogCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "brownfield-log");
    }
}
