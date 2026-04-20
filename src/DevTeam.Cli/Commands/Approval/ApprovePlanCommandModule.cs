namespace DevTeam.Cli;

internal sealed class ApprovePlanCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "approve", "approve-plan");
    }
}
