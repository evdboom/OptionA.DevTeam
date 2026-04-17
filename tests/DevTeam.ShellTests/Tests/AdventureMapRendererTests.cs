using DevTeam.Cli.Shell;
using DevTeam.Core;
using DevTeam.ShellTests;
using Spectre.Console.Testing;

namespace DevTeam.ShellTests.Tests;

internal static class AdventureMapRendererTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("HelpMarkup_HidesAdventureByDefault", HelpMarkup_HidesAdventureByDefault),
        new("HelpMarkup_All_RevealsAdventure", HelpMarkup_All_RevealsAdventure),
        new("BuildWorld_PrioritizesActiveRolesAndBubbles", BuildWorld_PrioritizesActiveRolesAndBubbles),
        new("MapPanel_RendersPlayerAndSpeechBubble", MapPanel_RendersPlayerAndSpeechBubble),
        new("AdjacentDesk_FindsNearbyDesk", AdjacentDesk_FindsNearbyDesk),
    ];

    private static Task HelpMarkup_HidesAdventureByDefault()
    {
        var markup = ShellService.BuildInteractiveHelpMarkup();
        Assert.That(!markup.Contains("/adventure"), $"Did not expect hidden adventure command in default help: {markup}");
        return Task.CompletedTask;
    }

    private static Task HelpMarkup_All_RevealsAdventure()
    {
        var markup = ShellService.BuildInteractiveHelpMarkup(showAll: true);
        Assert.That(markup.Contains("/adventure"), $"Expected hidden adventure command in --all help: {markup}");
        return Task.CompletedTask;
    }

    private static Task BuildWorld_PrioritizesActiveRolesAndBubbles()
    {
        var snapshot = new AdventureShellSnapshot(
            true,
            WorkflowPhase.Execution,
            [
                new AdventureRoleSlot("planner", "planner"),
                new AdventureRoleSlot("architect", "architect"),
                new AdventureRoleSlot("developer", "developer"),
                new AdventureRoleSlot("tester", "tester"),
                new AdventureRoleSlot("security", "security"),
                new AdventureRoleSlot("docs", "docs"),
                new AdventureRoleSlot("auditor", "auditor"),
            ],
            [
                new AgentSlot(1, 7, "architect", "Design API", AgentRunStatus.Running)
            ],
            [
                new RoadmapSlot(8, "Implement API", "developer", ItemStatus.Open)
            ],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tester"] = "Need one more repro"
            });

        var world = AdventureMapRenderer.BuildWorld(snapshot);

        Assert.That(world.Desks.Count == 6, $"Expected 6 visible desks, got {world.Desks.Count}");
        Assert.That(world.Desks.Any(desk => desk.RoleSlug == "architect"), "Expected active architect desk to be visible.");
        Assert.That(world.Desks.Any(desk => desk.RoleSlug == "tester" && desk.BubbleText == "Need one more repro"), "Expected tester bubble to appear on the map.");
        Assert.That(world.HiddenRoles.Count == 1 && world.HiddenRoles[0] == "auditor", $"Expected one hidden role, got: {string.Join(", ", world.HiddenRoles)}");
        return Task.CompletedTask;
    }

    private static Task MapPanel_RendersPlayerAndSpeechBubble()
    {
        var snapshot = new AdventureShellSnapshot(
            true,
            WorkflowPhase.Execution,
            [new AdventureRoleSlot("architect", "architect")],
            [new AgentSlot(1, 7, "architect", "Design API", AgentRunStatus.Running)],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["architect"] = "API looks good"
            });

        var console = CreateConsole();
        console.Write(AdventureMapRenderer.BuildMapPanel(snapshot, new AdventurePoint(20, 6)));
        var output = console.Output;

        Assert.That(output.Contains("@"), $"Expected player marker in map output: {output}");
        Assert.That(output.Contains("API looks good"), $"Expected speech bubble in map output: {output}");
        return Task.CompletedTask;
    }

    private static Task AdjacentDesk_FindsNearbyDesk()
    {
        var snapshot = new AdventureShellSnapshot(
            true,
            WorkflowPhase.Execution,
            [new AdventureRoleSlot("architect", "architect")],
            [],
            [],
            new Dictionary<string, string>());

        var world = AdventureMapRenderer.BuildWorld(snapshot);
        var desk = world.Desks.Single();
        var nearby = AdventureMapRenderer.FindAdjacentDesk(world, new AdventurePoint(desk.Position.X + 1, desk.Position.Y));

        Assert.That(nearby is not null && nearby.RoleSlug == "architect", "Expected player beside the desk to be able to talk to architect.");
        return Task.CompletedTask;
    }

    private static TestConsole CreateConsole() =>
        new()
        {
            Profile =
            {
                Width = 120
            }
        };
}
