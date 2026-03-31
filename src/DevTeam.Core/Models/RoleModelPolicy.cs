namespace DevTeam.Core;

public sealed class RoleModelPolicy
{
    public string PrimaryModel { get; set; } = "";
    public string FallbackModel { get; set; } = "";
    public bool AllowPremium { get; set; }
}
