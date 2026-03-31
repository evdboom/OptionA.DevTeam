namespace DevTeam.Core;

public sealed class BudgetState
{
    public double TotalCreditCap { get; set; } = 25;
    public double PremiumCreditCap { get; set; } = 6;
    public double CreditsCommitted { get; set; }
    public double PremiumCreditsCommitted { get; set; }
}