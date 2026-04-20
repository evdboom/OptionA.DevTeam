namespace DevTeam.Cli;

internal sealed class SetAutoApproveCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "set-auto-approve");
    }
}
