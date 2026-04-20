using System.Diagnostics.CodeAnalysis;

namespace DevTeam.TestInfrastructure;

public static class DevTeamRuntimeCompleteRunCompatExtensions
{
    [SuppressMessage("Major Code Smell", "S107", Justification = "Compatibility extension preserves pre-refactor call shape for tests.")]
    public static void CompleteRun(
        this DevTeamRuntime runtime,
        WorkspaceState state,
        int runId,
        string outcome,
        string summary,
        IEnumerable<string>? skillsUsed = null,
        IEnumerable<string>? toolsUsed = null,
        IEnumerable<string>? changedPaths = null,
        IEnumerable<int>? createdIssueIds = null,
        IEnumerable<int>? createdQuestionIds = null,
        ItemStatus? resultingIssueStatus = null,
        int? inputTokens = null,
        int? outputTokens = null,
        double? estimatedCostUsd = null)
    {
        runtime.CompleteRun(
            state,
            new CompleteRunRequest
            {
                RunId = runId,
                Outcome = outcome,
                Summary = summary,
                SkillsUsed = skillsUsed,
                ToolsUsed = toolsUsed,
                ChangedPaths = changedPaths,
                CreatedIssueIds = createdIssueIds,
                CreatedQuestionIds = createdQuestionIds,
                ResultingIssueStatus = resultingIssueStatus,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                EstimatedCostUsd = estimatedCostUsd
            });
    }
}
