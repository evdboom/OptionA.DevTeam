namespace DevTeam.Cli;

internal sealed class DispatchRegistrationService
{
    private readonly CliCommandRegistry _registry = new();

    public DispatchRegistrationService(IEnumerable<ICliCommandModule> modules)
    {
        foreach (var module in modules)
        {
            module.Register(_registry);
        }
    }

    public bool TryResolve(string command, out ICliCommandHandler handler) =>
        _registry.TryResolve(command, out handler);
}
