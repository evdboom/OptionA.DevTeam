using DevTeam.Cli;
using DevTeam.Core;

namespace DevTeam.UnitTests.Tests.Commands;

internal static class SetGoalCommandHandlerTests
{
    private const string RoverGoal = "build a rover";

    public static IEnumerable<TestCase> GetTests() =>
    [
        new("ExecuteAsync_SetsGoal_WhenGoalProvided", ExecuteAsync_SetsGoal_WhenGoalProvided),
        new("ExecuteAsync_ThrowsInvalidOp_WhenNoGoalProvided", ExecuteAsync_ThrowsInvalidOp_WhenNoGoalProvided),
        new("ExecuteAsync_PrintsSuccessMessage", ExecuteAsync_PrintsSuccessMessage),
        new("ExecuteAsync_SavesStateToWorkspace", ExecuteAsync_SavesStateToWorkspace),
    ];

    private static async Task ExecuteAsync_SetsGoal_WhenGoalProvided()
    {
        var output = new FakeConsoleOutput();
        var tempDir = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new WorkspaceStore(tempDir);
            var runtime = new DevTeamRuntime();
            var handler = new SetGoalCommandHandler(store, runtime, output);
            
            store.Initialize(tempDir, 25, 6);
            var options = new Dictionary<string, List<string>>
            {
                ["__positional"] = new List<string> { RoverGoal }
            };

            var result = await handler.ExecuteAsync(options);

            Assert.That(result == 0, $"Expected exit code 0 but got {result}");
            var state = store.Load();
            Assert.That(state.ActiveGoal?.GoalText == RoverGoal, $"Expected goal '{RoverGoal}' but got '{state.ActiveGoal?.GoalText}'");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* Best-effort temp cleanup. */ }
        }
    }

    private static async Task ExecuteAsync_ThrowsInvalidOp_WhenNoGoalProvided()
    {
        var output = new FakeConsoleOutput();
        var tempDir = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new WorkspaceStore(tempDir);
            var runtime = new DevTeamRuntime();
            var handler = new SetGoalCommandHandler(store, runtime, output);

            store.Initialize(tempDir, 25, 6);
            var options = new Dictionary<string, List<string>>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(options));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* Best-effort temp cleanup. */ }
        }
    }

    private static Task ExecuteAsync_PrintsSuccessMessage()
    {
        var output = new FakeConsoleOutput();
        var tempDir = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new WorkspaceStore(tempDir);
            var runtime = new DevTeamRuntime();
            var handler = new SetGoalCommandHandler(store, runtime, output);

            store.Initialize(tempDir, 25, 6);
            var options = new Dictionary<string, List<string>>
            {
                ["__positional"] = new List<string> { RoverGoal }
            };

            handler.ExecuteAsync(options).Wait();

            Assert.That(output.Lines.Count > 0, "Expected output lines");
            Assert.That(output.Lines[0].Contains("Updated active goal"), $"Expected success message but got '{output.Lines[0]}'");
            return Task.CompletedTask;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* Best-effort temp cleanup. */ }
        }
    }

    private static Task ExecuteAsync_SavesStateToWorkspace()
    {
        var output = new FakeConsoleOutput();
        var tempDir = Path.Combine(Path.GetTempPath(), $"devteam-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new WorkspaceStore(tempDir);
            var runtime = new DevTeamRuntime();
            var handler = new SetGoalCommandHandler(store, runtime, output);

            store.Initialize(tempDir, 25, 6);
            var options = new Dictionary<string, List<string>>
            {
                ["__positional"] = new List<string> { RoverGoal }
            };

            handler.ExecuteAsync(options).Wait();

            // Create a new store instance to verify persistence
            var store2 = new WorkspaceStore(tempDir);
            var state2 = store2.Load();
            Assert.That(state2.ActiveGoal?.GoalText == RoverGoal, $"Expected persisted goal '{RoverGoal}' but got '{state2.ActiveGoal?.GoalText}'");
            return Task.CompletedTask;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* Best-effort temp cleanup. */ }
        }
    }
}
