using DevTeam.Cli;
using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;

namespace DevTeam.ShellTests.Tests;

internal static class SprintResumeHintTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("NoHint_WhenNoActiveIssues", NoHint_WhenNoActiveIssues),
        new("NoHint_WhenAllIssuesAreDone", NoHint_WhenAllIssuesAreDone),
        new("ShowsHint_WhenOpenExecutionIssues", ShowsHint_WhenOpenExecutionIssues),
        new("ShowsHint_WhenInProgressIssues", ShowsHint_WhenInProgressIssues),
        new("ShowsWarning_ForInProgressIssues", ShowsWarning_ForInProgressIssues),
        new("HintContainsIssueCount", HintContainsIssueCount),
        new("HintContainsPhase_ArchitectPlanning", HintContainsPhase_ArchitectPlanning),
        new("SprintResumeScenario_HasOpenAndInProgressIssues", SprintResumeScenario_HasOpenAndInProgressIssues),
    ];

    private static ShellService BuildShell(WorkspaceState state, string workspacePath)
    {
        var store = new WorkspaceStore(workspacePath);
        store.Save(state);
        var runtime = new DevTeamRuntime();
        var executor = new LoopExecutor(runtime, store);
        var tus = new ToolUpdateService(); // intentionally not disposed here; shell owns lifetime
        return new ShellService(store, runtime, executor, tus, new ShellStartOptions([]), () => { });
    }

    private static string Wp() => Path.Combine(Path.GetTempPath(), $"devteam-sprinttest-{Guid.NewGuid():N}");

    private static Task NoHint_WhenNoActiveIssues()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildEmptyScenario(wp);
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var messages = shell.Messages;
            var hints = messages.Where(m => m.Markup.Contains("sprint item")).ToList();
            Assert.That(hints.Count == 0, $"Expected no sprint resume hint, got {hints.Count}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task NoHint_WhenAllIssuesAreDone()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildBaseState(wp);
            state.Phase = WorkflowPhase.Execution;
            state.Issues.AddRange([
                new IssueItem { Id = 1, Title = "Design", RoleSlug = "architect", Status = ItemStatus.Done },
                new IssueItem { Id = 2, Title = "Implement", RoleSlug = "developer", Status = ItemStatus.Done },
            ]);
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var hints = shell.Messages.Where(m => m.Markup.Contains("sprint item")).ToList();
            Assert.That(hints.Count == 0, $"Expected no sprint resume hint, got {hints.Count}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ShowsHint_WhenOpenExecutionIssues()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildBaseState(wp);
            state.Phase = WorkflowPhase.Execution;
            state.Issues.Add(new IssueItem { Id = 1, Title = "Implement feature", RoleSlug = "developer", Status = ItemStatus.Open });
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var hints = shell.Messages.Where(m => m.Markup.Contains("sprint item")).ToList();
            Assert.That(hints.Count == 1, $"Expected 1 sprint resume hint, got {hints.Count}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ShowsHint_WhenInProgressIssues()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildBaseState(wp);
            state.Phase = WorkflowPhase.Execution;
            state.Issues.Add(new IssueItem { Id = 1, Title = "Implement feature", RoleSlug = "developer", Status = ItemStatus.InProgress });
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var hints = shell.Messages.Where(m => m.Markup.Contains("sprint item")).ToList();
            Assert.That(hints.Count == 1, $"Expected 1 sprint resume hint, got {hints.Count}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task ShowsWarning_ForInProgressIssues()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildBaseState(wp);
            state.Phase = WorkflowPhase.Execution;
            state.Issues.Add(new IssueItem { Id = 1, Title = "Running task", RoleSlug = "developer", Status = ItemStatus.InProgress });
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var warnings = shell.Messages.Where(m => m.Markup.Contains("in progress")).ToList();
            Assert.That(warnings.Count >= 1, $"Expected warning about in-progress issue, got: {string.Join(", ", shell.Messages.Select(m => m.Markup))}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task HintContainsIssueCount()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildBaseState(wp);
            state.Phase = WorkflowPhase.Execution;
            state.Issues.AddRange([
                new IssueItem { Id = 1, Title = "Task 1", RoleSlug = "developer", Status = ItemStatus.Open },
                new IssueItem { Id = 2, Title = "Task 2", RoleSlug = "developer", Status = ItemStatus.Open },
                new IssueItem { Id = 3, Title = "Task 3", RoleSlug = "developer", Status = ItemStatus.Done },
            ]);
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var hint = shell.Messages.FirstOrDefault(m => m.Markup.Contains("sprint item"));
            Assert.That(hint is not null, "Expected sprint resume hint");
            Assert.That(hint!.Markup.Contains("2"), $"Expected hint to mention '2' open issues, got: {hint.Markup}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task HintContainsPhase_ArchitectPlanning()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildBaseState(wp);
            state.Phase = WorkflowPhase.ArchitectPlanning;
            state.Issues.Add(new IssueItem { Id = 1, Title = "Arch work", RoleSlug = "architect", Status = ItemStatus.Open });
            using var shell = BuildShell(state, wp);

            shell.ShowSprintResumeHint(state);

            var hint = shell.Messages.FirstOrDefault(m => m.Markup.Contains("sprint item"));
            Assert.That(hint is not null, "Expected sprint resume hint for ArchitectPlanning phase");
            Assert.That(hint!.Markup.Contains("architect planning"), $"Expected phase name in hint, got: {hint.Markup}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }

    private static Task SprintResumeScenario_HasOpenAndInProgressIssues()
    {
        var wp = Wp();
        try
        {
            Directory.CreateDirectory(wp);
            var state = UiHarness.BuildSprintResumeScenario(wp);

            var openOrInProgress = state.Issues
                .Where(i => !i.IsPlanningIssue && (i.Status == ItemStatus.Open || i.Status == ItemStatus.InProgress))
                .ToList();

            Assert.That(openOrInProgress.Count >= 2, $"Expected at least 2 open/in-progress issues in sprint-resume scenario, got {openOrInProgress.Count}");

            var inProgress = openOrInProgress.Where(i => i.Status == ItemStatus.InProgress).ToList();
            Assert.That(inProgress.Count >= 1, $"Expected at least 1 in-progress issue (interrupted sprint), got {inProgress.Count}");
        }
        finally { try { Directory.Delete(wp, recursive: true); } catch { } }
        return Task.CompletedTask;
    }
}
