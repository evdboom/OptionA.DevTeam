namespace DevTeam.TestInfrastructure;

public sealed class FakeSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan by) => UtcNow += by;
}
