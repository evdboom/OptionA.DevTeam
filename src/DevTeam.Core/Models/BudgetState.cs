namespace DevTeam.Core;

public sealed class BudgetState
{
    /// <summary>Project-wide credit cap (all runs combined).</summary>
    public double TotalCreditCap { get; set; } = 25;
    
    /// <summary>Project-wide premium credit cap (expensive models).</summary>
    public double PremiumCreditCap { get; set; } = 6;
    
    /// <summary>Per-run standard credit limit (single iteration max spend).</summary>
    public double PerRunCreditLimit { get; set; } = 5;
    
    /// <summary>Per-run premium credit limit (single iteration max spend on expensive models).</summary>
    public double PerRunPremiumLimit { get; set; } = 2;
    
    /// <summary>Total credits committed across all runs.</summary>
    public double CreditsCommitted { get; set; }
    
    /// <summary>Total premium credits committed across all runs.</summary>
    public double PremiumCreditsCommitted { get; set; }
}