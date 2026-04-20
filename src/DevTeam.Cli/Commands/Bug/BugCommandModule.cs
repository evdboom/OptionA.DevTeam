namespace DevTeam.Cli;

internal sealed class BugCommandModule : ICliCommandModule
{
    private readonly ICliCommandHandler _bugReportHandler;

    public BugCommandModule(ICliCommandHandler bugReportHandler)
    {
        _bugReportHandler = bugReportHandler;
    }

    public void Register(ICliCommandRegistry registry)
    {
        registry.Register(_bugReportHandler, "bug", "bug-report");
    }
}
