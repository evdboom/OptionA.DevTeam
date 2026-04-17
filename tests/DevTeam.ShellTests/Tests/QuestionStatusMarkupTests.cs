using DevTeam.Cli;
using DevTeam.ShellTests;
using DevTeam.Core;

namespace DevTeam.ShellTests.Tests;

internal static class QuestionStatusMarkupTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildQuestionLineMarkup_IncludesAgeAndBlockingState", BuildQuestionLineMarkup_IncludesAgeAndBlockingState),
        new("DescribeLoopState_WaitingForUser_IsFriendly", DescribeLoopState_WaitingForUser_IsFriendly),
    ];

    private static Task BuildQuestionLineMarkup_IncludesAgeAndBlockingState()
    {
        var question = new QuestionItem
        {
            Id = 7,
            Text = "Should we keep the old API route?",
            IsBlocking = true
        };
        var markup = WorkspaceStatusPrinter.BuildQuestionLineMarkup(question, new Dictionary<int, TimeSpan>
        {
            [7] = TimeSpan.FromHours(2)
        });

        Assert.That(markup.Contains("#7") && markup.Contains("blocking") && markup.Contains("asked 2h ago"),
            $"Expected question markup to include id, blocking state, and age, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task DescribeLoopState_WaitingForUser_IsFriendly()
    {
        var label = WorkspaceStatusPrinter.DescribeLoopState("waiting-for-user");

        Assert.That(label == "waiting for user input",
            $"Expected friendly waiting label but got: {label}");
        return Task.CompletedTask;
    }
}
