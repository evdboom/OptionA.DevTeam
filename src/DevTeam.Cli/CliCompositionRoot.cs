using DevTeam.Core;

namespace DevTeam.Cli;

internal static class CliCompositionRoot
{
    internal static CliDispatcher CreateDispatcher(string workspacePath, ToolUpdateService toolUpdateService)
    {
        var store = new WorkspaceStore(workspacePath);
        var runtime = new DevTeamRuntime();
        var loopExecutor = new LoopExecutor(runtime, store);
        var output = new ConsoleOutput();

        var dispatchRegistration = new DispatchRegistrationService(new ICliCommandModule[]
        {
            new BugCommandModule(new BugReportCommandHandler(store, runtime)),
            new PlanCommandModule(new PlanCommandHandler(store, runtime, loopExecutor)),
            new RunCommandModule(
                new RunLoopCommandHandler(store, runtime, loopExecutor, output),
                new RunOnceCommandHandler(store, runtime, output))
        });

        return new CliDispatcher(store, runtime, loopExecutor, toolUpdateService, workspacePath, dispatchRegistration, output);
    }
}
