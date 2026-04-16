using DevTeam.Cli;

namespace DevTeam.UnitTests.Tests;

internal static class GitHubIssueSyncServiceTests
{
    private sealed class ScriptedCommandRunner(params CommandExecutionResult[] responses) : ICommandRunner
    {
        private readonly Queue<CommandExecutionResult> _responses = new(responses);

        public List<CommandExecutionSpec> Calls { get; } = [];

        public Task<CommandExecutionResult> RunAsync(CommandExecutionSpec spec, CancellationToken cancellationToken = default)
        {
            Calls.Add(spec);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No scripted command response available.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    public static IEnumerable<TestCase> GetTests() =>
    [
        new("SyncAsync_ImportsIssuesQuestionsAndDependencies", SyncAsync_ImportsIssuesQuestionsAndDependencies),
        new("SyncAsync_UpdatesExistingItemsAndSkipsClosedOnes", SyncAsync_UpdatesExistingItemsAndSkipsClosedOnes),
        new("SyncAsync_ThrowsWhenGitHubAuthMissing", SyncAsync_ThrowsWhenGitHubAuthMissing)
    ];

    private static async Task SyncAsync_ImportsIssuesQuestionsAndDependencies()
    {
        var runtime = new DevTeamRuntime();
        var state = CreateState();
        var runner = new ScriptedCommandRunner(
            new CommandExecutionResult { ExitCode = 0 },
            new CommandExecutionResult
            {
                ExitCode = 0,
                StdOut = """
                    [
                      {
                        "number": 101,
                        "title": "Review queue import",
                        "body": "---\nrole: reviewer\npriority: 90\narea: repo sync\ndepends: 102\n---\nReview the imported GitHub issue.",
                        "labels": [{ "name": "devteam:ready" }]
                      },
                      {
                        "number": 102,
                        "title": "Implement queue import",
                        "body": "Import GitHub issues into the local workspace.",
                        "labels": [{ "name": "devteam:ready" }, { "name": "role:developer" }, { "name": "priority:80" }]
                      },
                      {
                        "number": 103,
                        "title": "Clarify the release workflow",
                        "body": "Please confirm the release checklist.",
                        "labels": [{ "name": "devteam:question" }, { "name": "devteam:blocking" }]
                      },
                      {
                        "number": 104,
                        "title": "Ignore me",
                        "body": "Not part of the synced queue.",
                        "labels": []
                      }
                    ]
                    """
            });

        var service = new GitHubIssueSyncService(runner);
        var report = await service.SyncAsync(state, runtime, FindRepoRoot());

        Assert.That(runner.Calls.Count == 2, $"Expected 2 gh calls but saw {runner.Calls.Count}.");
        Assert.That(runner.Calls[0].Arguments.SequenceEqual(["auth", "status"]), "First gh call should check auth status.");
        Assert.That(runner.Calls[1].Arguments.SequenceEqual(["issue", "list", "--state", "open", "--limit", "100", "--json", "number,title,body,labels"]),
            "Second gh call should list open issues.");
        Assert.That(report.ImportedIssueCount == 2, $"Expected 2 imported issues but got {report.ImportedIssueCount}.");
        Assert.That(report.ImportedQuestionCount == 1, $"Expected 1 imported question but got {report.ImportedQuestionCount}.");
        Assert.That(report.SkippedCount == 1, $"Expected 1 skipped issue but got {report.SkippedCount}.");

        var dependent = state.Issues.Single(issue => issue.ExternalReference == "github#101");
        var dependency = state.Issues.Single(issue => issue.ExternalReference == "github#102");
        var question = state.Questions.Single(item => item.ExternalReference == "github#103");

        Assert.That(dependent.RoleSlug == "reviewer", $"Expected reviewer role but got {dependent.RoleSlug}.");
        Assert.That(dependent.Priority == 90, $"Expected priority 90 but got {dependent.Priority}.");
        Assert.That(dependent.Area == "repo-sync", $"Expected normalized area repo-sync but got {dependent.Area}.");
        Assert.That(dependent.DependsOnIssueIds.SequenceEqual([dependency.Id]), "Expected imported dependency mapping.");
        Assert.That(question.IsBlocking, "Question should be marked blocking from GitHub labels.");
        Assert.That(question.Text.Contains("Clarify the release workflow", StringComparison.Ordinal), "Question text should include the GitHub title.");
        Assert.That(state.Decisions.Any(item => item.Source == "github-sync"), "Sync should record a decision artifact entry.");
    }

    private static async Task SyncAsync_UpdatesExistingItemsAndSkipsClosedOnes()
    {
        var runtime = new DevTeamRuntime();
        var state = CreateState();
        var updatableIssue = runtime.AddIssue(state, "Old title", "Old detail", "developer", 50, null, []);
        updatableIssue.ExternalReference = "github#201";

        var doneIssue = runtime.AddIssue(state, "Done title", "Done detail", "developer", 50, null, []);
        doneIssue.ExternalReference = "github#202";
        doneIssue.Status = ItemStatus.Done;

        var openQuestion = runtime.AddQuestion(state, "Old question", true);
        openQuestion.ExternalReference = "github#203";

        var answeredQuestion = runtime.AddQuestion(state, "Answered question", true);
        answeredQuestion.ExternalReference = "github#204";
        answeredQuestion.Status = QuestionStatus.Answered;

        var runner = new ScriptedCommandRunner(
            new CommandExecutionResult { ExitCode = 0 },
            new CommandExecutionResult
            {
                ExitCode = 0,
                StdOut = """
                    [
                      {
                        "number": 201,
                        "title": "Updated issue",
                        "body": "---\nrole: reviewer\npriority: 95\narea: repo sync\n---\nUpdated detail.",
                        "labels": [{ "name": "devteam:ready" }]
                      },
                      {
                        "number": 202,
                        "title": "Should stay done",
                        "body": "Done issue should be skipped.",
                        "labels": [{ "name": "devteam:ready" }]
                      },
                      {
                        "number": 203,
                        "title": "Updated question",
                        "body": "Need a fresh answer.",
                        "labels": [{ "name": "devteam:question" }]
                      },
                      {
                        "number": 204,
                        "title": "Should stay answered",
                        "body": "Already answered locally.",
                        "labels": [{ "name": "devteam:question" }, { "name": "devteam:blocking" }]
                      }
                    ]
                    """
            });

        var service = new GitHubIssueSyncService(runner);
        var report = await service.SyncAsync(state, runtime, FindRepoRoot());

        Assert.That(report.UpdatedIssueCount == 1, $"Expected 1 updated issue but got {report.UpdatedIssueCount}.");
        Assert.That(report.UpdatedQuestionCount == 1, $"Expected 1 updated question but got {report.UpdatedQuestionCount}.");
        Assert.That(report.SkippedCount == 2, $"Expected 2 skipped items but got {report.SkippedCount}.");
        Assert.That(updatableIssue.RoleSlug == "reviewer", $"Expected reviewer role but got {updatableIssue.RoleSlug}.");
        Assert.That(updatableIssue.Priority == 95, $"Expected priority 95 but got {updatableIssue.Priority}.");
        Assert.That(updatableIssue.Area == "repo-sync", $"Expected normalized area repo-sync but got {updatableIssue.Area}.");
        Assert.That(updatableIssue.Title == "Updated issue", $"Expected updated title but got {updatableIssue.Title}.");
        Assert.That(openQuestion.Text.Contains("Updated question", StringComparison.Ordinal), "Expected updated question text.");
        Assert.That(!openQuestion.IsBlocking, "Question should update blocking state when not marked blocking.");
    }

    private static async Task SyncAsync_ThrowsWhenGitHubAuthMissing()
    {
        var runtime = new DevTeamRuntime();
        var state = CreateState();
        var runner = new ScriptedCommandRunner(new CommandExecutionResult { ExitCode = 1, StdErr = "not logged in" });
        var service = new GitHubIssueSyncService(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SyncAsync(state, runtime, FindRepoRoot()),
            "Expected missing GitHub auth to fail the sync.");
    }

    private static WorkspaceState CreateState() => SeedData.BuildInitialState(FindRepoRoot(), 25, 6);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".devteam-source")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for tests.");
    }
}
