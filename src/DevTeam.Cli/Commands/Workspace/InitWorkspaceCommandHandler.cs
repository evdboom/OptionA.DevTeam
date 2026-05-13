using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;
using static DevTeam.Cli.CliWorkspaceHelper;

namespace DevTeam.Cli;

internal sealed class InitWorkspaceCommandHandler(
    WorkspaceStore store,
    DevTeamRuntime runtime,
    IConsoleOutput output,
    string workspacePath,
    IWorkspaceReconRunner reconRunner) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;
    private readonly string _workspacePath = workspacePath;
    private readonly IWorkspaceReconRunner _reconRunner = reconRunner;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var force = GetBoolOption(options, "force", false);
        if (!force && File.Exists(_store.StatePath))
        {
            _output.WriteErrorLine($"Workspace already initialized at {Path.GetFullPath(_workspacePath)}.");
            _output.WriteErrorLine("Use --force to reinitialize (this will reset all workspace state).");
            return 1;
        }
        var totalCap = GetDoubleOption(options, "total-credit-cap", 50);
        var premiumCap = GetDoubleOption(options, "premium-credit-cap", 25);
        var goal = GoalInputResolver.Resolve(
            GetOption(options, "goal") ?? GetPositionalValue(options),
            GetOption(options, "goal-file"),
            Environment.CurrentDirectory);
        var gitInitialized = GitWorkspace.EnsureRepository(Environment.CurrentDirectory);
        var gitignoreUpdated = GitWorkspace.EnsureDevTeamGitignore(Environment.CurrentDirectory);
        var state = _store.Initialize(Environment.CurrentDirectory, totalCap, premiumCap);
        var mode = GetOption(options, "mode");
        state.Runtime.KeepAwakeEnabled = GetBoolOption(options, "keep-awake", state.Runtime.KeepAwakeEnabled);
        state.Runtime.WorkspaceMcpEnabled = GetBoolOption(options, "workspace-mcp", true);
        state.Runtime.PipelineSchedulingEnabled = GetBoolOption(options, "pipeline-scheduling", true);
        state.Runtime.AutoApproveEnabled = GetBoolOption(options, "auto-approve", state.Runtime.AutoApproveEnabled);
        if (!string.IsNullOrWhiteSpace(mode))
        {
            _runtime.SetMode(state, mode);
        }
        var provider = GetOption(options, "provider");
        if (!string.IsNullOrWhiteSpace(provider))
        {
            ProviderSelectionService.SetDefaultProvider(state, provider);
        }
        if (!string.IsNullOrWhiteSpace(goal))
        {
            _runtime.SetGoal(state, goal);
        }
        _store.Save(state);
        CliWorkspaceHelper.ExportGitHubSkills(Environment.CurrentDirectory, force, _output.WriteLine);

        _output.WriteLine($"Initialized devteam workspace at {Path.GetFullPath(_workspacePath)}");
        if (gitInitialized)
        {
            _output.WriteLine($"Initialized git repository at {Path.GetFullPath(Environment.CurrentDirectory)}");
        }
        if (gitignoreUpdated)
        {
            _output.WriteLine("Updated .gitignore with DevTeam runtime workspace rules.");
        }
        if (!string.IsNullOrWhiteSpace(goal))
        {
            _output.WriteLine($"Active goal saved: {goal}");
        }

        var isNonEmptyRepo = Directory.EnumerateFileSystemEntries(Environment.CurrentDirectory)
            .Any(f => !Path.GetFileName(f).StartsWith('.'));
        var runRecon = GetBoolOption(options, "recon", isNonEmptyRepo);
        if (runRecon)
        {
            var backend = GetOption(options, "backend") ?? "sdk";
            var timeout = TimeSpan.FromSeconds(GetIntOption(options, "timeout-seconds", 120));
            _output.WriteLine("Running codebase reconnaissance...");
            var context = await _reconRunner.RunAsync(state, _store, backend, timeout, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(context))
            {
                _output.WriteLine("Project map / codebase context written to .devteam/codebase-context.md");
            }
        }
        return 0;
    }
}
