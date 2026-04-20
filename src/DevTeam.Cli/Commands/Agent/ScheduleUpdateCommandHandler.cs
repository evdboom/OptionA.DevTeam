namespace DevTeam.Cli;

internal sealed class ScheduleUpdateCommandHandler(ToolUpdateService toolUpdateService) : ICliCommandHandler
{
    private readonly ToolUpdateService _toolUpdateService = toolUpdateService;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options) =>
        await UpdateCommandHandler.ScheduleToolUpdateAsync(_toolUpdateService, interactiveShell: false);
}
