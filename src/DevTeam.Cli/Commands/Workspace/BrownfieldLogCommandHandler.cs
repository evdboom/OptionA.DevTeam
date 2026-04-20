namespace DevTeam.Cli;

internal sealed class BrownfieldLogCommandHandler(string workspacePath, IConsoleOutput output) : ICliCommandHandler
{
    private readonly string _workspacePath = workspacePath;
    private readonly IConsoleOutput _output = output;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var path = Path.Combine(_workspacePath, "brownfield-delta.md");
        if (!File.Exists(path))
        {
            _output.WriteLine("No brownfield delta log yet.");
            return 0;
        }

        var content = await File.ReadAllTextAsync(path);
        _output.WriteLine(content);
        return 0;
    }
}
