namespace DevTeam.Cli.Shell;

/// <summary>Captures the parsed options from the <c>devteam start</c> CLI invocation.</summary>
internal sealed record ShellStartOptions(Dictionary<string, List<string>> Options);
