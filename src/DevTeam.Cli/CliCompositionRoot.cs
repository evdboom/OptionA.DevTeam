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
        var workspaceMcpHost = new WorkspaceMcpHost();
        var githubSyncOrchestrator = new GitHubIssueSyncOrchestrator(new GitHubIssueSyncService());
        var agentClientFactory = new DefaultAgentClientFactory();
        var configurationLoader = new FileSystemConfigurationLoader();
        var reconRunner = new WorkspaceReconRunner(new ReconService(agentClientFactory));

        var modules = new ICliCommandModule[]
        {
            // Issues & Roadmap
            new AddIssueCommandModule(new AddIssueCommandHandler(store, runtime, output)),
            new EditIssueCommandModule(new EditIssueCommandHandler(store, runtime, output)),
            new AddRoadmapCommandModule(new AddRoadmapCommandHandler(store, runtime, output)),

            // Questions
            new AddQuestionCommandModule(new AddQuestionCommandHandler(store, runtime, output)),
            new AnswerQuestionCommandModule(new AnswerQuestionCommandHandler(store, runtime, output)),

            // Approval
            new ApprovePlanCommandModule(new ApprovePlanCommandHandler(store, runtime, output)),
            new FeedbackCommandModule(new FeedbackCommandHandler(store, runtime, output)),
            new SetAutoApproveCommandModule(new SetAutoApproveCommandHandler(store, runtime, output)),

            // Settings
            new SetGoalCommandModule(new SetGoalCommandHandler(store, runtime, output)),
            new SetModeCommandModule(new SetModeCommandHandler(store, runtime, output)),
            new PipelineCommandModule(new PipelineCommandHandler(store, output)),
            new SetPipelineCommandModule(new SetPipelineCommandHandler(store, runtime, output)),
            new ProviderCommandModule(new ProviderCommandHandler(store, output)),
            new SetProviderCommandModule(new SetProviderCommandHandler(store, output)),
            new SetKeepAwakeCommandModule(new SetKeepAwakeCommandHandler(store, runtime, output)),
            new BudgetCommandModule(new BudgetCommandHandler(store)),

            // Workspace
            new InitWorkspaceCommandModule(new InitWorkspaceCommandHandler(store, runtime, output, workspacePath, reconRunner)),
            new CustomizeWorkspaceCommandModule(new CustomizeWorkspaceCommandHandler(output)),
            new ExportWorkspaceCommandModule(new ExportWorkspaceCommandHandler(workspacePath, output)),
            new ImportWorkspaceCommandModule(new ImportWorkspaceCommandHandler(workspacePath, output)),
            new GitHubSyncCommandModule(new GitHubSyncCommandHandler(store, runtime, output, githubSyncOrchestrator)),
            new BrownfieldLogCommandModule(new BrownfieldLogCommandHandler(workspacePath, output)),
            new WorkspaceMcpCommandModule(new WorkspaceMcpCommandHandler(workspaceMcpHost)),

            // Plan & Run
            new PlanCommandModule(new PlanCommandHandler(store, runtime, loopExecutor)),
            new RunCommandModule(
                new RunLoopCommandHandler(store, runtime, loopExecutor, output),
                new RunOnceCommandHandler(store, runtime, output)),

            // Query & Status
            new StatusCommandModule(new StatusCommandHandler(store, runtime)),
            new QuestionsCommandModule(new QuestionsCommandHandler(store, runtime)),
            new PreviewCommandModule(new PreviewCommandHandler(store, runtime, output)),
            new DiffRunCommandModule(new DiffRunCommandHandler(store, runtime, output)),
            new CompleteRunCommandModule(new CompleteRunCommandHandler(store, runtime, output)),
            new StartHereCommandModule(new StartHereCommandHandler(store, runtime, output)),

            // Agent & Tools
            new BugCommandModule(new BugReportCommandHandler(store, runtime)),
            new AgentInvokeCommandModule(new AgentInvokeCommandHandler(store, workspacePath, output, agentClientFactory, configurationLoader)),
            new CheckUpdateCommandModule(new CheckUpdateCommandHandler(toolUpdateService, output)),
            new ScheduleUpdateCommandModule(new ScheduleUpdateCommandHandler(toolUpdateService)),
            new UiHarnessCommandModule(new UiHarnessCommandHandler()),
            new StartShellCommandModule(new StartShellCommandHandler(store, runtime, loopExecutor, toolUpdateService)),
            new HelpCommandModule(new HelpCommandHandler())
        };

        var dispatchRegistration = new DispatchRegistrationService(modules);
        return new CliDispatcher(dispatchRegistration);
    }
}
