using System.Runtime.InteropServices;

namespace DevTeam.Cli;

internal sealed class KeepAwakeController : IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _loopTask;

    public bool IsEnabled { get; private set; }

    public static bool IsSupported => OperatingSystem.IsWindows();

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled)
        {
            return;
        }

        if (enabled)
        {
            if (!IsSupported)
            {
                throw new InvalidOperationException("Keep-awake is only supported on Windows.");
            }

            SignalKeepAwake();
            _cancellationTokenSource = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunKeepAwakeLoopAsync(_cancellationTokenSource.Token));
            IsEnabled = true;
            return;
        }

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _loopTask = null;
        IsEnabled = false;
    }

    public void Dispose()
    {
        SetEnabled(false);
    }

    private static async Task RunKeepAwakeLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                SignalKeepAwake();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void SignalKeepAwake()
    {
        if (SetThreadExecutionState(ExecutionState.SystemRequired | ExecutionState.DisplayRequired) == 0)
        {
            throw new InvalidOperationException("Windows rejected the keep-awake request.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern uint SetThreadExecutionState(ExecutionState executionState);

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002
    }
}
