using DevTeam.Cli;
using DevTeam.Core;

var command = CliOptionParser.NormalizeCommand(args.Length == 0 ? "help" : args[0]);
var options = CliOptionParser.ParseOptions(args.Skip(1).ToArray());
var workspacePath = CliOptionParser.GetOption(options, "workspace") ?? ".devteam";
var store = new WorkspaceStore(workspacePath);
var runtime = new DevTeamRuntime();
var loopExecutor = new LoopExecutor(runtime, store);
using var toolUpdateService = new ToolUpdateService();

try
{
    CommandOptionValidator.ValidateCli(command, options);
    var dispatcher = new CliDispatcher(store, runtime, loopExecutor, toolUpdateService, workspacePath);
    return await dispatcher.DispatchAsync(command, options);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
