namespace DevTeam.Cli;

internal sealed class PlanCommandModule : ICliCommandModule
{
    private readonly ICliCommandHandler _planHandler;

    public PlanCommandModule(ICliCommandHandler planHandler)
    {
        _planHandler = planHandler;
    }

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_planHandler, "plan");
    }
}
