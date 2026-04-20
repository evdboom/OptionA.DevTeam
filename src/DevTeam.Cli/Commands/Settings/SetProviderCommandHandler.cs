using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class SetProviderCommandHandler(WorkspaceStore store, IConsoleOutput output) : ICliCommandHandler
{
    private readonly WorkspaceStore _store = store;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var state = _store.Load();
        var providerName = GetPositionalValue(options) ?? throw new InvalidOperationException("Usage: set-provider <name|default>");
        ProviderSelectionService.SetDefaultProvider(
            state,
            string.Equals(providerName, "default", StringComparison.OrdinalIgnoreCase) ? null : providerName);
        _store.Save(state);
        _output.WriteLine(string.IsNullOrWhiteSpace(state.Runtime.DefaultProviderName)
            ? "Reset provider override to default Copilot auth."
            : $"Updated default provider to {state.Runtime.DefaultProviderName}.");
        return Task.FromResult(0);
    }
}
