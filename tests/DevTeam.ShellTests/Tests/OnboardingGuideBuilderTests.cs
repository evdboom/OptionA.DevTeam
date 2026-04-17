using DevTeam.Cli;
using DevTeam.Core;
using DevTeam.ShellTests;

namespace DevTeam.ShellTests.Tests;

internal static class OnboardingGuideBuilderTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("BuildMarkup_NoWorkspaceNewPersona_ShowsInitFlow", BuildMarkup_NoWorkspaceNewPersona_ShowsInitFlow),
        new("BuildMarkup_PlanningWorkspace_ShowsPlanNextStep", BuildMarkup_PlanningWorkspace_ShowsPlanNextStep),
        new("BuildMarkup_ExpertPersona_ShowsCustomizationGuidance", BuildMarkup_ExpertPersona_ShowsCustomizationGuidance),
    ];

    private static Task BuildMarkup_NoWorkspaceNewPersona_ShowsInitFlow()
    {
        var markup = OnboardingGuideBuilder.BuildMarkup(state: null, new DevTeamRuntime(), "new");

        Assert.That(markup.Contains("Start here") && markup.Contains("/init") && markup.Contains("/max-subagents 1"),
            $"Expected no-workspace onboarding guide to show init flow and safe defaults, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task BuildMarkup_PlanningWorkspace_ShowsPlanNextStep()
    {
        var state = UiHarness.BuildBaseState("C:\\temp");
        state.Phase = WorkflowPhase.Planning;
        state.Issues.Clear();

        var markup = OnboardingGuideBuilder.BuildMarkup(state, new DevTeamRuntime(), "medior");

        Assert.That(markup.Contains("Next step:") && markup.Contains("/plan"),
            $"Expected planning onboarding guide to point to /plan, got: {markup}");
        return Task.CompletedTask;
    }

    private static Task BuildMarkup_ExpertPersona_ShowsCustomizationGuidance()
    {
        var state = UiHarness.BuildBaseState("C:\\temp");

        var markup = OnboardingGuideBuilder.BuildMarkup(state, new DevTeamRuntime(), "expert");

        Assert.That(markup.Contains("/customize") && markup.Contains("/mode autopilot"),
            $"Expected expert onboarding guide to mention customization and autopilot, got: {markup}");
        return Task.CompletedTask;
    }
}
