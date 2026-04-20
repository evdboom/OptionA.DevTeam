using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class PipelineCommandHandler(WorkspaceStore store, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var source = state.Runtime.PipelineRolesCustomized ? "custom" : $"mode default ({state.Runtime.ActiveModeSlug})";
        _output.WriteLine($"Current pipeline: {string.Join(" -> ", state.Runtime.DefaultPipelineRoles)} [{source}]");
        return Task.FromResult(0);
    }
}
