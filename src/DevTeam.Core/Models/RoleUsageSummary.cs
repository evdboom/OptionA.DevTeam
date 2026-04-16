namespace DevTeam.Core;

public sealed class RoleUsageSummary
{
    public string RoleSlug { get; init; } = "";
    public int RunCount { get; init; }
    public int CompletedRunCount { get; init; }
    public double CreditsUsed { get; init; }
    public double PremiumCreditsUsed { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public double? EstimatedCostUsd { get; init; }
}
