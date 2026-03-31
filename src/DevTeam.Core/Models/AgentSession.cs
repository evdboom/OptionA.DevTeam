namespace DevTeam.Core;

public sealed class AgentSession
{
    public string ScopeKey { get; set; } = "";
    public string ScopeKind { get; set; } = "";
    public string RoleSlug { get; set; } = "";
    public int? IssueId { get; set; }
    public int? PipelineId { get; set; }
    public int? LastRunId { get; set; }
    public string SessionId { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
