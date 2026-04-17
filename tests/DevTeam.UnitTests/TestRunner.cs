using DevTeam.UnitTests.Tests;

namespace DevTeam.UnitTests;

internal sealed record TestCase(string Name, Func<Task> Body);
internal sealed record TestResults(int Passed, int Failed);

internal static class TestRunner
{
    public static async Task<TestResults> RunAllAsync()
    {
        var r1 = await RunSuiteAsync("IssueServiceTests", IssueServiceTests.GetTests());
        var r2 = await RunSuiteAsync("QuestionServiceTests", QuestionServiceTests.GetTests());
        var r3 = await RunSuiteAsync("RoadmapServiceTests", RoadmapServiceTests.GetTests());
        var r4 = await RunSuiteAsync("BudgetServiceTests", BudgetServiceTests.GetTests());
        var r5 = await RunSuiteAsync("PlanningServiceTests", PlanningServiceTests.GetTests());
        var r6 = await RunSuiteAsync("SessionManagerTests", SessionManagerTests.GetTests());
        var r7 = await RunSuiteAsync("WorkspaceStoreTests", WorkspaceStoreTests.GetTests());
        var r8 = await RunSuiteAsync("FileSystemConfigurationLoaderTests", FileSystemConfigurationLoaderTests.GetTests());
        var r9 = await RunSuiteAsync("LoopExecutorTests", LoopExecutorTests.GetTests());
        var r10 = await RunSuiteAsync("WorkspaceMcpServerTests", WorkspaceMcpServerTests.GetTests());
        var r11 = await RunSuiteAsync("CursorNavigationTests", CursorNavigationTests.GetTests());
        var r12 = await RunSuiteAsync("WorktreeLifecycleTests", WorktreeLifecycleTests.GetTests());
        var r13 = await RunSuiteAsync("ReconServiceTests", ReconServiceTests.GetTests());
        var r14 = await RunSuiteAsync("RunPreviewTests", RunPreviewTests.GetTests());
        var r15 = await RunSuiteAsync("StatusReportTests", StatusReportTests.GetTests());
        var r16 = await RunSuiteAsync("RunDiffTests", RunDiffTests.GetTests());
        var r17 = await RunSuiteAsync("GitHubIssueSyncServiceTests", GitHubIssueSyncServiceTests.GetTests());

        var passed = r1.Passed + r2.Passed + r3.Passed + r4.Passed + r5.Passed + r6.Passed + r7.Passed + r8.Passed + r9.Passed + r10.Passed + r11.Passed + r12.Passed + r13.Passed + r14.Passed + r15.Passed + r16.Passed + r17.Passed;
        var failed = r1.Failed + r2.Failed + r3.Failed + r4.Failed + r5.Failed + r6.Failed + r7.Failed + r8.Failed + r9.Failed + r10.Failed + r11.Failed + r12.Failed + r13.Failed + r14.Failed + r15.Failed + r16.Failed + r17.Failed;

        Console.WriteLine();
        Console.WriteLine($"Results: {passed} passed, {failed} failed");
        return new TestResults(passed, failed);
    }

    internal static async Task<(int Passed, int Failed)> RunSuiteAsync(string name, IEnumerable<TestCase> tests)
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
