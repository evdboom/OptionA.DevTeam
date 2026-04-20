namespace DevTeam.Cli;

internal sealed class CheckUpdateCommandHandler(ToolUpdateService toolUpdateService, IConsoleOutput output) : ICliCommandHandler
{
    private readonly ToolUpdateService _toolUpdateService = toolUpdateService;
    private readonly IConsoleOutput _output = output;

    public async Task<int> ExecuteAsync(Dictionary<string, List<string>> options)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ToolUpdateStatus status;
        try
        {
            status = await _toolUpdateService.CheckAsync(timeoutCts.Token);
        }
        catch (ToolUpdateUnavailableException ex)
        {
            _output.WriteLine(ex.Message);
            return 0;
        }
        catch (HttpRequestException)
        {
            _output.WriteLine("Update check is unavailable right now.");
            return 0;
        }
        catch (TaskCanceledException)
        {
            _output.WriteLine("Update check timed out.");
            return 0;
        }

        if (status.IsUpdateAvailable)
        {
            _output.WriteLine($"Update available: {status.LatestVersion} (current {status.CurrentVersion}).");
            _output.WriteLine("Run `devteam update` or `/update` in the shell to install it.");
            return 0;
        }

        _output.WriteLine($"OptionA.DevTeam is up to date ({status.CurrentVersion}).");
        return 0;
    }
}
