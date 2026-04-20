namespace DevTeam.Cli;

internal sealed class CompleteRunCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "complete-run");
    }
}
