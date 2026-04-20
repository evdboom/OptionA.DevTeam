using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class AddRoadmapCommandHandler(WorkspaceStore store, DevTeamRuntime runtime, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly DevTeamRuntime _runtime = runtime;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var title = GetPositionalValue(options) ?? throw new InvalidOperationException("Missing roadmap title.");
        var detail = GetOption(options, "detail") ?? "";
        var priority = GetIntOption(options, "priority", 50);
        var item = _runtime.AddRoadmapItem(state, title, detail, priority);
        _store.Save(state);
        _output.WriteLine($"Created roadmap item #{item.Id}: {item.Title}");
        return Task.FromResult(0);
    }
}
