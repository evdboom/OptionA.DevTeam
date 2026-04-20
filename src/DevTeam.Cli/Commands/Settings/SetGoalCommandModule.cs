namespace DevTeam.Cli;

internal sealed class SetGoalCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "goal", "set-goal");
    }
}
