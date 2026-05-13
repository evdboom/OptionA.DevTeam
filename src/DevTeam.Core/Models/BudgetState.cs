namespace DevTeam.Core;

public sealed class BudgetState
{
    /// <summary>Project-wide credit cap (all runs combined).</summary>
    public double TotalCreditCap { get; set; } = 50;
    
    /// <summary>Project-wide premium credit cap (expensive models).</summary>
    public double PremiumCreditCap { get; set; } = 25;
    
    /// <summary>Per-run standard credit limit (single iteration max spend).</summary>
    public double PerRunCreditLimit { get; set; } = 5;
    
    /// <summary>
    /// Per-run premium credit limit (single iteration max spend on expensive models).
    /// Higher than <see cref="PerRunCreditLimit"/> because premium models (e.g. gpt-5.5 at 7.5,
    /// claude-opus-4.7 at 15) are significantly more expensive than standard models.
    /// </summary>
    public double PerRunPremiumLimit { get; set; } = 15;
    
    /// <summary>Total credits committed across all runs.</summary>
    public double CreditsCommitted { get; set; }
    
    /// <summary>Total premium credits committed across all runs.</summary>
    public double PremiumCreditsCommitted { get; set; }
}