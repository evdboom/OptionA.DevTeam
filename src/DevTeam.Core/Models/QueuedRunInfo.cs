namespace DevTeam.Core;

public sealed class QueuedRunInfo
{
    public int RunId { get; init; }
    public int IssueId { get; init; }
    public string Title { get; init; } = "";
    public string RoleSlug { get; init; } = "";
    public string Area { get; init; } = "";
    public string ModelName { get; init; } = "";
}
