namespace DevTeam.Core;

public sealed class RoadmapService : IRoadmapService
{
    public RoadmapItem AddRoadmapItem(WorkspaceState state, string title, string detail, int priority)
    {
        var item = new RoadmapItem
        {
            Id = state.NextRoadmapId++,
            Title = title.Trim(),
            Detail = detail.Trim(),
            Priority = priority
        };
        state.Roadmap.Add(item);
        return item;
    }
}
