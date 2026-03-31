using Microsoft.AspNetCore.Components;

namespace DevTeam.Cli.Shell;

public partial class DevTeamShell : IDisposable
{
    [Inject] private ShellService Shell { get; set; } = null!;

    private string? _input;
    private IReadOnlyList<ShellMessage> _visibleMessages = [];

    protected override void OnInitialized()
    {
        Shell.OnStateChanged += Refresh;
        UpdateVisible();
        _ = Shell.InitializeAsync();
    }

    private void UpdateVisible()
    {
        // Keep the last N messages so the input line is always in view.
        // Each line message costs ~1 row; each panel costs ~3+. Use a conservative
        // row budget of (terminal height - 3) to leave room for the input widget.
        var budget = Math.Max(5, Console.WindowHeight - 3);
        var all = Shell.Messages;
        _visibleMessages = all.Count <= budget ? all : all.TakeLast(budget).ToList();
    }

    private void Refresh() => _ = InvokeAsync(() => { UpdateVisible(); StateHasChanged(); });

    private async Task HandleSubmitAsync(string? line)
    {
        _input = null;
        if (!string.IsNullOrWhiteSpace(line))
            await Shell.ProcessInputAsync(line.Trim());
    }

    public void Dispose() => Shell.OnStateChanged -= Refresh;
}
