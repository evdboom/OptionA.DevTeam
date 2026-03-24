namespace DevTeam.Cli;

internal sealed class ShellSessionDiagnostics
{
    private const int MaxEntries = 20;
    private readonly List<ShellSessionEntry> _commands = [];
    private readonly List<ShellSessionEntry> _errors = [];

    public void RecordCommand(string command) => Add(_commands, command);

    public void RecordError(string error) => Add(_errors, error);

    public IReadOnlyList<ShellSessionEntry> GetRecentCommands(int count) => GetRecent(_commands, count);

    public IReadOnlyList<ShellSessionEntry> GetRecentErrors(int count) => GetRecent(_errors, count);

    private static IReadOnlyList<ShellSessionEntry> GetRecent(List<ShellSessionEntry> entries, int count)
    {
        var take = Math.Max(1, count);
        return entries.TakeLast(take).ToArray();
    }

    private static void Add(List<ShellSessionEntry> entries, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        entries.Add(new ShellSessionEntry(DateTimeOffset.UtcNow, text.Trim()));
        if (entries.Count > MaxEntries)
        {
            entries.RemoveAt(0);
        }
    }
}

internal sealed record ShellSessionEntry(DateTimeOffset TimestampUtc, string Text);
