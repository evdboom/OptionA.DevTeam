namespace DevTeam.Cli;

internal sealed class DiffRunCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "diff-run");
    }
}
