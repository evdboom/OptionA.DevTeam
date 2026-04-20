using DevTeam.Cli;

namespace DevTeam.UnitTests.Tests.Commands;

internal static class WorkspaceMcpCommandHandlerTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("ExecuteAsync_UsesProvidedOptions", ExecuteAsync_UsesProvidedOptions),
        new("ExecuteAsync_UsesDefaultValues", ExecuteAsync_UsesDefaultValues),
        new("ExecuteAsync_ReturnsHostExitCode", ExecuteAsync_ReturnsHostExitCode),
    ];

    private static async Task ExecuteAsync_UsesProvidedOptions()
    {
        var host = new FakeWorkspaceMcpHost(0);
        var handler = new WorkspaceMcpCommandHandler(host);
        var options = new Dictionary<string, List<string>>
        {
            ["workspace"] = ["custom.devteam"],
            ["backend"] = ["cli"],
            ["timeout-seconds"] = ["42"]
        };

        await handler.ExecuteAsync(options);

        Assert.That(host.CallCount == 1, $"Expected one host invocation but got {host.CallCount}");
        Assert.That(host.LastWorkspacePath == "custom.devteam", $"Expected workspace 'custom.devteam' but got '{host.LastWorkspacePath}'");
        Assert.That(host.LastBackend == "cli", $"Expected backend 'cli' but got '{host.LastBackend}'");
        Assert.That(host.LastTimeout == TimeSpan.FromSeconds(42), $"Expected timeout 42s but got {host.LastTimeout}");
    }

    private static async Task ExecuteAsync_UsesDefaultValues()
    {
        var host = new FakeWorkspaceMcpHost(0);
        var handler = new WorkspaceMcpCommandHandler(host);

        await handler.ExecuteAsync(new Dictionary<string, List<string>>());

        Assert.That(host.LastWorkspacePath == ".devteam", $"Expected default workspace '.devteam' but got '{host.LastWorkspacePath}'");
        Assert.That(host.LastBackend == "sdk", $"Expected default backend 'sdk' but got '{host.LastBackend}'");
        Assert.That(host.LastTimeout == TimeSpan.FromSeconds(600), $"Expected default timeout 600s but got {host.LastTimeout}");
    }

    private static async Task ExecuteAsync_ReturnsHostExitCode()
    {
        var host = new FakeWorkspaceMcpHost(7);
        var handler = new WorkspaceMcpCommandHandler(host);

        var result = await handler.ExecuteAsync(new Dictionary<string, List<string>>());

        Assert.That(result == 7, $"Expected result 7 but got {result}");
    }

    private sealed class FakeWorkspaceMcpHost : IWorkspaceMcpHost
    {
        private readonly int _result;

        public FakeWorkspaceMcpHost(int result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public string LastWorkspacePath { get; private set; } = "";
        public string LastBackend { get; private set; } = "";
        public TimeSpan LastTimeout { get; private set; }

        public Task<int> RunAsync(string workspacePath, string backend, TimeSpan timeout)
        {
            CallCount++;
            LastWorkspacePath = workspacePath;
            LastBackend = backend;
            LastTimeout = timeout;
            return Task.FromResult(_result);
        }
    }
}
