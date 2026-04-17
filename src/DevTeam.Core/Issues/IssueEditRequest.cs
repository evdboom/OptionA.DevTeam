namespace DevTeam.Core;

public sealed class IssueEditRequest
{
    public required int IssueId { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public string? RoleSlug { get; init; }
    public string? Area { get; init; }
    public bool ClearArea { get; init; }
    public int? Priority { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<int>? DependsOnIssueIds { get; init; }
    public bool ClearDependencies { get; init; }
    public string? NotesToAppend { get; init; }

    public bool HasChanges =>
        Title is not null
        || Detail is not null
        || RoleSlug is not null
        || Area is not null
        || ClearArea
        || Priority is not null
        || Status is not null
        || DependsOnIssueIds is not null
        || ClearDependencies
        || !string.IsNullOrWhiteSpace(NotesToAppend);
}
