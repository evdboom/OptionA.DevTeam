namespace DevTeam.Cli;

internal sealed class CliCommandRegistry : ICliCommandRegistry
{
    private readonly Dictionary<string, ICliCommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ICliCommandHandler handler, params string[] commands)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (commands.Length == 0)
        {
            throw new InvalidOperationException($"Handler {handler.GetType().Name} must register at least one command.");
        }

        foreach (var command in commands)
        {
            var normalized = CliOptionParser.NormalizeCommand(command);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException($"Handler {handler.GetType().Name} registered an empty command.");
            }

            if (_handlers.TryGetValue(normalized, out var existing))
            {
                throw new InvalidOperationException(
                    $"Command '{normalized}' is already registered by {existing.GetType().Name}. Cannot register {handler.GetType().Name}.");
            }

            _handlers[normalized] = handler;
        }
    }

    public bool TryResolve(string command, out ICliCommandHandler handler)
    {
        var normalized = CliOptionParser.NormalizeCommand(command);
        if (_handlers.TryGetValue(normalized, out var resolved))
        {
            handler = resolved;
            return true;
        }

        handler = null!;
        return false;
    }
}
