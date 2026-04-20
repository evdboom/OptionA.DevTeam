namespace DevTeam.Cli;

internal sealed class ExportWorkspaceCommandHandler(string workspacePath, IConsoleOutput output) : ICliCommandHandler
{
    private readonly string _workspacePath = workspacePath;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var outputPath = CliOptionParser.GetOption(options, "output");
        var archivePath = WorkspaceArchiveService.Export(_workspacePath, outputPath);
        _output.WriteLine($"Exported workspace to {archivePath}");
        return Task.FromResult(0);
    }
}
