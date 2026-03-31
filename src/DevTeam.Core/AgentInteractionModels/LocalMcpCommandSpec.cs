namespace DevTeam.Core;

public sealed class LocalMcpCommandSpec
{
    public string Command { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
}