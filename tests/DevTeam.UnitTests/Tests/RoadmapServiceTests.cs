namespace DevTeam.UnitTests.Tests;

internal static class RoadmapServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("AddRoadmapItem_AssignsIncrementingId", AddRoadmapItem_AssignsIncrementingId),
        new("AddRoadmapItem_StoresTitleAndDetail", AddRoadmapItem_StoresTitleAndDetail),
        new("AddRoadmapItem_StoresPriority", AddRoadmapItem_StoresPriority),
    ];

    private static Task AddRoadmapItem_AssignsIncrementingId()
    {
        var svc = new RoadmapService();
        var state = new WorkspaceState();

        var r1 = svc.AddRoadmapItem(state, "First", "detail", 80);
        var r2 = svc.AddRoadmapItem(state, "Second", "detail", 60);

        Assert.That(r1.Id == 1, $"Expected id 1 but got {r1.Id}");
        Assert.That(r2.Id == 2, $"Expected id 2 but got {r2.Id}");
        return Task.CompletedTask;
    }

    private static Task AddRoadmapItem_StoresTitleAndDetail()
    {
        var svc = new RoadmapService();
        var state = new WorkspaceState();

        var item = svc.AddRoadmapItem(state, "  My Feature  ", "  Deliver value.  ", 50);

        Assert.That(item.Title == "My Feature", $"Expected trimmed title but got '{item.Title}'");
        Assert.That(item.Detail == "Deliver value.", $"Expected trimmed detail but got '{item.Detail}'");
        return Task.CompletedTask;
    }

    private static Task AddRoadmapItem_StoresPriority()
    {
        var svc = new RoadmapService();
        var state = new WorkspaceState();

        var item = svc.AddRoadmapItem(state, "High-pri", "detail", 95);

        Assert.That(item.Priority == 95, $"Expected priority 95 but got {item.Priority}");
        Assert.That(state.Roadmap.Count == 1, $"Expected 1 roadmap item but got {state.Roadmap.Count}");
        return Task.CompletedTask;
    }
}
