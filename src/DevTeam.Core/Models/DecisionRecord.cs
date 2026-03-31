namespace DevTeam.Core;

public sealed class DecisionRecord
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Source { get; set; } = "";
    public int? IssueId { get; set; }
    public int? RunId { get; set; }
    public string SessionId { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
