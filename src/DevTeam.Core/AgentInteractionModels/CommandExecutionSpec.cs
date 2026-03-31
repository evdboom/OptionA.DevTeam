namespace DevTeam.Core;

public sealed class CommandExecutionSpec
{
    public string FileName { get; init; } = "";
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(20);
}