namespace DevTeam.Cli;

internal sealed class RunCommandModule : ICliCommandModule
{
    private readonly ICliCommandHandler _runLoopHandler;
    private readonly ICliCommandHandler _runOnceHandler;

    public RunCommandModule(ICliCommandHandler runLoopHandler, ICliCommandHandler runOnceHandler)
    {
        _runLoopHandler = runLoopHandler;
        _runOnceHandler = runOnceHandler;
    }

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_runLoopHandler, "run", "run-loop");
        registry.Register(_runOnceHandler, "run-once");
    }
}
