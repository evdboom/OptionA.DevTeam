using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class AgentInvokeCommandHandler(
    WorkspaceStore store,
    string workspacePath,
    IConsoleOutput output,
    IAgentClientFactory agentClientFactory,
    IConfigurationLoader configurationLoader) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly string _workspacePath = workspacePath;
    private readonly IConsoleOutput _output = output;
    private readonly IAgentClientFactory _agentClientFactory = agentClientFactory;
    private readonly IConfigurationLoader _configurationLoader = configurationLoader;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var backend = GetOption(options, "backend") ?? "sdk";
        var prompt = GetOption(options, "prompt") ?? GetPositionalValue(options)
            ?? throw new InvalidOperationException("Missing prompt text.");
        var model = GetOption(options, "model");
        var providerName = GetOption(options, "provider");
        var timeoutSeconds = GetIntOption(options, "timeout-seconds", 1200);
        var workingDirectory = GetOption(options, "working-directory") ?? Environment.CurrentDirectory;
        var extraArgs = options.TryGetValue("extra-arg", out var values)
            ? values
            : [];
        var providerState = File.Exists(_store.StatePath)
            ? _store.Load()
            : new WorkspaceState
            {
                RepoRoot = Environment.CurrentDirectory,
                Runtime = RuntimeConfiguration.CreateDefault(),
                Models = _configurationLoader.LoadModels(Environment.CurrentDirectory),
                Providers = _configurationLoader.LoadProviders(Environment.CurrentDirectory)
            };
        var provider = ProviderSelectionService.ResolveProvider(providerState, model, providerName);

        var client = _agentClientFactory.Create(backend);
        var result = await client.InvokeAsync(new AgentInvocationRequest
        {
            Prompt = prompt,
            Model = model,
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            ExtraArguments = extraArgs,
            WorkspacePath = Path.GetFullPath(_workspacePath),
            Provider = provider,
            EnableWorkspaceMcp = GetBoolOption(options, "workspace-mcp", false),
            ToolHostPath = System.Reflection.Assembly.GetEntryAssembly()?.Location
        });

        _output.WriteLine($"Backend: {result.BackendName}");
        _output.WriteLine($"Exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            _output.WriteLine(result.StdOut.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            _output.WriteErrorLine(result.StdErr.TrimEnd());
        }
        return result.Success ? 0 : result.ExitCode;
    }
}
