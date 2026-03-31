namespace DevTeam.Core;

public sealed class ExecutionSelectionState
{
    public List<int> SelectedIssueIds { get; set; } = [];
    public string Rationale { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
