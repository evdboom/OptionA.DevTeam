namespace DevTeam.Core;

public sealed record RunProgressSnapshot(
    int? IssueId,
    string RoleSlug,
    string Title,
    TimeSpan Elapsed);
