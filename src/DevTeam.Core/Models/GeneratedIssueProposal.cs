namespace DevTeam.Core;

public sealed class GeneratedIssueProposal
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string RoleSlug { get; init; } = "developer";
    public string Area { get; init; } = "";
    public int Priority { get; init; } = 50;
    public IReadOnlyList<int> DependsOnIssueIds { get; init; } = [];
}
