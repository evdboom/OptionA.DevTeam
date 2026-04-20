namespace DevTeam.Cli;

internal sealed class AddRoadmapCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "add-roadmap");
    }
}
