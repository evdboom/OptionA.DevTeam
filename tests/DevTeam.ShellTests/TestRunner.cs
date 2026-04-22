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
        var r6 = await RunSuiteAsync("SprintResumeHintTests", SprintResumeHintTests.GetTests());
        var r7 = await RunSuiteAsync("WorkflowGuideMarkupTests", WorkflowGuideMarkupTests.GetTests());
        var r8 = await RunSuiteAsync("QuestionStatusMarkupTests", QuestionStatusMarkupTests.GetTests());
        var r9 = await RunSuiteAsync("OnboardingGuideBuilderTests", OnboardingGuideBuilderTests.GetTests());
        var r10 = await RunSuiteAsync("AdventureMapRendererTests", AdventureMapRendererTests.GetTests());
        var r11 = await RunSuiteAsync("TerminalMouseScrollTests", TerminalMouseScrollTests.GetTests());
        var r12 = await RunSuiteAsync("ConnectCommandTests", ConnectCommandTests.GetTests());

        passed = r1.Passed + r2.Passed + r3.Passed + r4.Passed + r5.Passed + r6.Passed + r7.Passed + r8.Passed + r9.Passed + r10.Passed + r11.Passed + r12.Passed;
        failed = r1.Failed + r2.Failed + r3.Failed + r4.Failed + r5.Failed + r6.Failed + r7.Failed + r8.Failed + r9.Failed + r10.Failed + r11.Failed + r12.Failed;

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
