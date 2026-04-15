namespace DevTeam.Core;

public interface IRoadmapService
{
    RoadmapItem AddRoadmapItem(WorkspaceState state, string title, string detail, int priority);
}
