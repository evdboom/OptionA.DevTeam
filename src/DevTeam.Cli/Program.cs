using DevTeam.Cli;

var command = CliOptionParser.NormalizeCommand(args.Length == 0 ? "help" : args[0]);
var options = CliOptionParser.ParseOptions(args.Skip(1).ToArray());
var workspacePath = CliOptionParser.NormalizeWorkspacePathOrThrow(CliOptionParser.GetOption(options, "workspace"));
using var toolUpdateService = new ToolUpdateService();

try
{
    CommandOptionValidator.ValidateCli(command, options);
    var dispatcher = CliCompositionRoot.CreateDispatcher(workspacePath, toolUpdateService);
    return await dispatcher.DispatchAsync(command, options);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync(ex.Message);
    return 1;
}
