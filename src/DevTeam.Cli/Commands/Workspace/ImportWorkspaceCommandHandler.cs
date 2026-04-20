using static DevTeam.Cli.CliOptionParser;

namespace DevTeam.Cli;

internal sealed class ImportWorkspaceCommandHandler(string workspacePath, IConsoleOutput output) : ICliCommandHandler
{
    private readonly string _workspacePath = workspacePath;
    private readonly IConsoleOutput _output = output;

    public Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        var inputPath = GetOption(options, "input") ?? GetPositionalValue(options)
            ?? throw new InvalidOperationException("Usage: import --input PATH [--force] [--workspace PATH]");
        var importedPath = WorkspaceArchiveService.Import(inputPath, _workspacePath, GetBoolOption(options, "force", false));
        _output.WriteLine($"Imported workspace into {importedPath}");
        return Task.FromResult(0);
    }
}
