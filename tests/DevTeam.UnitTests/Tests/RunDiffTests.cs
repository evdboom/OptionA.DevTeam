namespace DevTeam.UnitTests.Tests;

internal static class RunDiffTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildRunDiff_SingleRun_IncludesCreatedItemsAndChangedPaths", BuildRunDiff_SingleRun_IncludesCreatedItemsAndChangedPaths),
        new("BuildRunDiff_CompareRuns_SplitsChangedPaths", BuildRunDiff_CompareRuns_SplitsChangedPaths),
    ];

    private static Task BuildRunDiff_SingleRun_IncludesCreatedItemsAndChangedPaths()
    {
        var runtime = new DevTeamRuntime();
        var state = new WorkspaceState();
        state.Issues.AddRange(
        [
            new IssueItem { Id = 1, Title = "Implement UI", RoleSlug = "developer", Area = "ui", Status = ItemStatus.Done },
            new IssueItem { Id = 2, Title = "Test UI", RoleSlug = "tester", Area = "ui", Status = ItemStatus.Open }
        ]);
        state.Questions.Add(new QuestionItem { Id = 1, Text = "Which theme?", IsBlocking = true });
        state.AgentRuns.Add(new AgentRun
        {
            Id = 10,
            IssueId = 1,
            Status = AgentRunStatus.Completed,
            Summary = "Implemented the UI slice.",
            ResultingIssueStatus = ItemStatus.Done,
            ChangedPaths = ["src/Ui.cs", "tests/UiTests.cs"],
            CreatedIssueIds = [2],
            CreatedQuestionIds = [1]
        });

        var report = runtime.BuildRunDiff(state, 10);

        Assert.That(report.PrimaryRun.Id == 10, $"Expected run 10 but got {report.PrimaryRun.Id}");
        Assert.That(report.PrimaryCreatedIssues.Count == 1 && report.PrimaryCreatedIssues[0].Id == 2, "Expected created issue #2 in report.");
        Assert.That(report.PrimaryCreatedQuestions.Count == 1 && report.PrimaryCreatedQuestions[0].Id == 1, "Expected created question #1 in report.");
        Assert.That(report.PrimaryOnlyChangedPaths.SequenceEqual(["src/Ui.cs", "tests/UiTests.cs"]), "Expected single-run changed paths to be preserved.");
        return Task.CompletedTask;
    }

    private static Task BuildRunDiff_CompareRuns_SplitsChangedPaths()
    {
        var runtime = new DevTeamRuntime();
        var state = new WorkspaceState();
        state.Issues.AddRange(
        [
            new IssueItem { Id = 1, Title = "Implement UI", RoleSlug = "developer", Area = "ui", Status = ItemStatus.Done },
            new IssueItem { Id = 2, Title = "Implement API", RoleSlug = "developer", Area = "api", Status = ItemStatus.Done }
        ]);
        state.AgentRuns.AddRange(
        [
            new AgentRun
            {
                Id = 10,
                IssueId = 1,
                Status = AgentRunStatus.Completed,
                ChangedPaths = ["src/Shared.cs", "src/Ui.cs"]
            },
            new AgentRun
            {
                Id = 11,
                IssueId = 2,
                Status = AgentRunStatus.Completed,
                ChangedPaths = ["src/Shared.cs", "src/Api.cs"]
            }
        ]);

        var report = runtime.BuildRunDiff(state, 11, 10);

        Assert.That(report.SharedChangedPaths.SequenceEqual(["src/Shared.cs"]), "Expected shared changed path.");
        Assert.That(report.PrimaryOnlyChangedPaths.SequenceEqual(["src/Api.cs"]), "Expected run 11 unique path.");
        Assert.That(report.CompareOnlyChangedPaths.SequenceEqual(["src/Ui.cs"]), "Expected run 10 unique path.");
        return Task.CompletedTask;
    }
}
