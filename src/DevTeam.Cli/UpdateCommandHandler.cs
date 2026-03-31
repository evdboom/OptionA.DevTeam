using System.Net.Http;

namespace DevTeam.Cli;

internal static class UpdateCommandHandler
{
    internal static async Task NotifyAboutAvailableUpdateAsync(ToolUpdateService toolUpdateService)
    {
        var status = await TryCheckForToolUpdatesAsync(toolUpdateService, TimeSpan.FromSeconds(3));
        if (status?.IsUpdateAvailable == true)
        {
            Console.WriteLine($"{ConsoleTheme.Warning("Update available:")} {ConsoleTheme.Number(status.LatestVersion)} (current {status.CurrentVersion}). Run {ConsoleTheme.Command("/update")} to install it.");
        }
    }

    internal static async Task<ToolUpdateStatus?> TryCheckForToolUpdatesAsync(ToolUpdateService toolUpdateService, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            return await toolUpdateService.CheckAsync(timeoutCts.Token);
        }
        catch (ToolUpdateUnavailableException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    internal static async Task<int> CheckForToolUpdatesAsync(ToolUpdateService toolUpdateService)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ToolUpdateStatus status;
        try
        {
            status = await toolUpdateService.CheckAsync(timeoutCts.Token);
        }
        catch (ToolUpdateUnavailableException ex)
        {
            Console.WriteLine(ex.Message);
            return 0;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Update check is unavailable right now.");
            return 0;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Update check timed out.");
            return 0;
        }

        if (status.IsUpdateAvailable)
        {
            Console.WriteLine($"Update available: {status.LatestVersion} (current {status.CurrentVersion}).");
            Console.WriteLine($"Run `devteam update` or `/update` in the shell to install it.");
            return 0;
        }

        Console.WriteLine($"OptionA.DevTeam is up to date ({status.CurrentVersion}).");
        return 0;
    }

    internal static async Task<int> ScheduleToolUpdateAsync(ToolUpdateService toolUpdateService, bool interactiveShell)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        ToolUpdateStatus status;
        try
        {
            status = await toolUpdateService.CheckAsync(timeoutCts.Token);
        }
        catch (ToolUpdateUnavailableException ex)
        {
            Console.WriteLine(ex.Message);
            return 0;
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("Update check is unavailable right now.");
            return 0;
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Update check timed out.");
            return 0;
        }

        if (!status.IsUpdateAvailable)
        {
            Console.WriteLine($"OptionA.DevTeam is already up to date ({status.CurrentVersion}).");
            return 0;
        }

        var launch = toolUpdateService.ScheduleGlobalUpdate(status.LatestVersion);
        Console.WriteLine($"Scheduling update to {status.LatestVersion} (current {status.CurrentVersion}).");
        Console.WriteLine($"If the updater does not complete, run `{launch.ManualCommand}`.");
        if (interactiveShell)
        {
            Console.WriteLine("Closing the shell so the updater can replace the installed tool.");
        }

        return 0;
    }
}
