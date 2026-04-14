namespace DevTeam.Core;

public sealed class RoleModelPolicy
{
    public string PrimaryModel { get; set; } = "";
    public string FallbackModel { get; set; } = "";
    public bool AllowPremium { get; set; }
    /// <summary>
    /// Optional pool of model names. When populated, the runtime picks one at random
    /// (filtered to affordable models) instead of always using PrimaryModel.
    /// PrimaryModel and FallbackModel still serve as the deterministic fallback chain.
    /// </summary>
    public List<string> ModelPool { get; set; } = [];
}
