using DevTeam.Core;

namespace DevTeam.Cli;

internal sealed class ProviderCommandHandler(WorkspaceStore store, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var currentProvider = string.IsNullOrWhiteSpace(state.Runtime.DefaultProviderName) ? "(default Copilot auth)" : state.Runtime.DefaultProviderName;
        var knownProviders = ProviderSelectionService.GetConfiguredProviderNames(state);
        _output.WriteLine($"Current provider: {currentProvider}");
        _output.WriteLine(knownProviders.Count == 0
            ? "Configured providers: none (.devteam-source\\PROVIDERS.json is empty or missing)"
            : $"Configured providers: {string.Join(", ", knownProviders)}");
        return Task.FromResult(0);
    }
}
