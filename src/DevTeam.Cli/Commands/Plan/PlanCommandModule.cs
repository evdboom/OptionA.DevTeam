namespace DevTeam.Cli;

internal sealed class PlanCommandModule(ICliCommandHandler planHandler) : ICliCommandModule
{
    private readonly ICliCommandHandler _planHandler = planHandler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_planHandler, "plan");
    }
}
