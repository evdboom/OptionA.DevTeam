namespace DevTeam.Cli;

internal sealed class BugCommandModule(ICliCommandHandler bugReportHandler) : ICliCommandModule
{
    private readonly ICliCommandHandler _bugReportHandler = bugReportHandler;

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_bugReportHandler, "bug", "bug-report");
    }
}
