namespace DevTeam.Core;

public sealed class GoalState
{
    public string GoalText { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}