using DevTeam.Core;
using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class CliDispatcher(
    DispatchRegistrationService dispatchRegistration)
{
    private readonly DispatchRegistrationService _dispatchRegistration = dispatchRegistration;

    public Task<int> DispatchAsync(string command, Dictionary<string, List<string>> options)
    {
        if (_dispatchRegistration.TryResolve(command, out var registeredHandler))
        {
            return registeredHandler.ExecuteAsync(options);
        }

        // Fallback: unknown command shows help
        WorkspaceStatusPrinter.PrintHelp(showAll: false);
        return Task.FromResult(1);
    }
}
