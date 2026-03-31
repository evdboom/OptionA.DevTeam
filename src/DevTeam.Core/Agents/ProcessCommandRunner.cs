using System.Diagnostics;
namespace DevTeam.Core;

public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = spec.FileName,
                WorkingDirectory = spec.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in spec.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{spec.FileName}'.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(spec.Timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);

        return new CommandExecutionResult
        {
            ExitCode = process.ExitCode,
            StdOut = await stdoutTask,
            StdErr = await stderrTask
        };
    }
}
