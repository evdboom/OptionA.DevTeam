namespace DevTeam.Cli;

internal sealed class SetKeepAwakeCommandModule(ICliCommandHandler handler) : ICliCommandModule
{
    private readonly ICliCommandHandler _handler = handler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_handler, "keep-awake", "set-keep-awake");
    }
}
