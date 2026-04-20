using DevTeam.Cli;

namespace DevTeam.UnitTests.Tests;

internal static class BugReportBuilderTests
{
    private const string WorkspaceFolder = ".devteam";

    public static IEnumerable<TestCase> GetTests() =>
    [
        new("Build_WhenWorkspaceMissing_ReportsNotLoaded", Build_WhenWorkspaceMissing_ReportsNotLoaded),
        new("Build_WhenWorkspaceLoadFails_ReportsFailure", Build_WhenWorkspaceLoadFails_ReportsFailure),
        new("Build_WithDiagnostics_RespectsRequestedCounts", Build_WithDiagnostics_RespectsRequestedCounts),
        new("Build_WithState_IncludesRecentRunSummary", Build_WithState_IncludesRecentRunSummary),
        new("Build_WithRedaction_RedactsWorkspacePath", Build_WithRedaction_RedactsWorkspacePath),
    ];

    private static Task Build_WhenWorkspaceMissing_ReportsNotLoaded()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var store = new WorkspaceStore(Path.Combine(tempDir, WorkspaceFolder));
            var runtime = new DevTeamRuntime();

            var report = BugReportBuilder.Build(store, runtime, shellDiagnostics: null, redactPaths: true, historyCount: 5, errorCount: 5);

            Assert.That(report.Contains("Workspace state file exists: no", StringComparison.Ordinal), "Expected state file absence marker.");
            Assert.That(report.Contains("Workspace load status: not loaded", StringComparison.Ordinal), "Expected not-loaded workspace status.");
            Assert.That(report.Contains("_No interactive shell command history was captured in this session._", StringComparison.Ordinal),
                "Expected empty-command-history marker.");
            return Task.CompletedTask;
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static Task Build_WhenWorkspaceLoadFails_ReportsFailure()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var workspacePath = Path.Combine(tempDir, WorkspaceFolder);
            Directory.CreateDirectory(workspacePath);
            var store = new WorkspaceStore(workspacePath);
            var runtime = new DevTeamRuntime();

            File.WriteAllText(store.StatePath, "{ not valid json }");

            var report = BugReportBuilder.Build(store, runtime, shellDiagnostics: null, redactPaths: true, historyCount: 5, errorCount: 5);

            Assert.That(report.Contains("Workspace load status: failed", StringComparison.Ordinal), "Expected failed workspace status.");
            Assert.That(report.Contains("Workspace load error:", StringComparison.Ordinal), "Expected workspace load error details.");
            return Task.CompletedTask;
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static Task Build_WithDiagnostics_RespectsRequestedCounts()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var store = new WorkspaceStore(Path.Combine(tempDir, WorkspaceFolder));
            var runtime = new DevTeamRuntime();
            var diagnostics = new ShellSessionDiagnostics();

            diagnostics.RecordCommand("/status");
            diagnostics.RecordCommand("/run");
            diagnostics.RecordCommand("/approve");
            diagnostics.RecordError("first error");
            diagnostics.RecordError("second error");

            var report = BugReportBuilder.Build(store, runtime, diagnostics, redactPaths: true, historyCount: 2, errorCount: 1);

            Assert.That(report.Contains("/run", StringComparison.Ordinal), "Expected second newest command.");
            Assert.That(report.Contains("/approve", StringComparison.Ordinal), "Expected newest command.");
            Assert.That(!report.Contains("/status", StringComparison.Ordinal), "Expected oldest command to be trimmed by count.");
            Assert.That(report.Contains("second error", StringComparison.Ordinal), "Expected newest error entry.");
            Assert.That(!report.Contains("first error", StringComparison.Ordinal), "Expected oldest error to be trimmed by count.");
            return Task.CompletedTask;
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static Task Build_WithState_IncludesRecentRunSummary()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var workspacePath = Path.Combine(tempDir, WorkspaceFolder);
            var store = new WorkspaceStore(workspacePath);
            var runtime = new DevTeamRuntime();
            var state = store.Initialize(tempDir, totalCreditCap: 25, premiumCreditCap: 6);

            runtime.SetGoal(state, "Validate bug report detail sections.");
            var issue = runtime.AddIssue(
                state,
                new IssueRequest
                {
                    Title = "Add reporting coverage",
                    Detail = "Exercise bug report builder branch coverage.",
                    RoleSlug = "developer",
                    Priority = 80,
                    RoadmapItemId = null,
                    DependsOn = [],
                    Area = "tests"
                });

            state.AgentRuns.Add(new AgentRun
            {
                Id = state.NextRunId++,
                IssueId = issue.Id,
                RoleSlug = issue.RoleSlug,
                ModelName = CoreConstants.Models.Gpt54Mini,
                Status = AgentRunStatus.Completed,
                Summary = "Implemented the new reporting tests.",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            store.Save(state);

            var report = BugReportBuilder.Build(store, runtime, shellDiagnostics: null, redactPaths: true, historyCount: 5, errorCount: 5);

            Assert.That(report.Contains("Workspace load status: loaded", StringComparison.Ordinal), "Expected loaded workspace marker.");
            Assert.That(report.Contains("Active goal: Validate bug report detail sections.", StringComparison.Ordinal), "Expected active goal in report.");
            Assert.That(report.Contains("Run #", StringComparison.Ordinal), "Expected run history entry.");
            Assert.That(report.Contains("Summary: Implemented the new reporting tests.", StringComparison.Ordinal), "Expected run summary text.");
            return Task.CompletedTask;
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static Task Build_WithRedaction_RedactsWorkspacePath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var workspacePath = Path.Combine(tempDir, WorkspaceFolder);
            var store = new WorkspaceStore(workspacePath);
            var runtime = new DevTeamRuntime();
            store.Initialize(tempDir, totalCreditCap: 25, premiumCreditCap: 6);

            var report = BugReportBuilder.Build(store, runtime, shellDiagnostics: null, redactPaths: true, historyCount: 5, errorCount: 5);

            Assert.That(report.Contains("<workspace>", StringComparison.Ordinal), "Expected redacted workspace token.");
            Assert.That(!report.Contains(workspacePath, StringComparison.OrdinalIgnoreCase), "Expected raw workspace path to be redacted.");
            return Task.CompletedTask;
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"devteam-bugreport-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup in tests.
        }
    }
}
