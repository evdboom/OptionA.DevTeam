using DevTeam.ShellTests.Tests;

namespace DevTeam.ShellTests;

internal sealed record TestCase(string Name, Func<Task> Body);
internal sealed record TestResults(int Passed, int Failed);

internal static class TestRunner
{
    public static async Task<TestResults> RunAllAsync()
    {
        var passed = 0;
        var failed = 0;

        var r1 = await RunSuiteAsync("ShellLayoutSnapshotTests", ShellLayoutSnapshotTests.GetTests());
        var r2 = await RunSuiteAsync("ShellPanelRenderTests", ShellPanelRenderTests.GetTests());
        var r3 = await RunSuiteAsync("UiHarnessScenarioTests", UiHarnessScenarioTests.GetTests());
        var r4 = await RunSuiteAsync("ProgressPanelScrollTests", ProgressPanelScrollTests.GetTests());
        var r5 = await RunSuiteAsync("NonInteractiveHostTests", NonInteractiveHostTests.GetTests());

        passed = r1.Passed + r2.Passed + r3.Passed + r4.Passed + r5.Passed;
        failed = r1.Failed + r2.Failed + r3.Failed + r4.Failed + r5.Failed;

        Console.WriteLine();
        Console.WriteLine($"Results: {passed} passed, {failed} failed");
        return new TestResults(passed, failed);
    }

    private static async Task<(int Passed, int Failed)> RunSuiteAsync(string name, IEnumerable<TestCase> tests)
    {
        Console.WriteLine($"Running {name}...");
        var passed = 0;
        var failed = 0;
        foreach (var testCase in tests)
        {
            try
            {
                await testCase.Body();
                Console.WriteLine($"  ✓ {testCase.Name}");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {testCase.Name}: {ex.Message}");
                failed++;
            }
        }
        return (passed, failed);
    }
}
