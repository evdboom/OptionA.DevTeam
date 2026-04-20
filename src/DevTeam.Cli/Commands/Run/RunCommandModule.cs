namespace DevTeam.Cli;

internal sealed class RunCommandModule(ICliCommandHandler runLoopHandler, ICliCommandHandler runOnceHandler) : ICliCommandModule
{
    private readonly ICliCommandHandler _runLoopHandler = runLoopHandler;
    private readonly ICliCommandHandler _runOnceHandler = runOnceHandler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_runLoopHandler, "run", "run-loop");
        registry.Register(_runOnceHandler, "run-once");
    }
}
