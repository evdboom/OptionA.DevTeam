namespace DevTeam.Core;

public sealed class BudgetService : IBudgetService
{
    private static readonly Random PoolRng = new();

    public ModelDefinition SelectModelForRole(WorkspaceState state, string roleSlug, string? excludeFamily = null)
    {
        var policy = SeedData.GetPolicy(state, roleSlug);
        var defaultModel = state.Models.FirstOrDefault(model => model.IsDefault) ?? new ModelDefinition
        {
            Name = "gpt-5-mini",
            Cost = 0
        };

        var remaining = state.Budget.TotalCreditCap - state.Budget.CreditsCommitted;
        var budgetRatio = state.Budget.TotalCreditCap > 0
            ? remaining / state.Budget.TotalCreditCap
            : 0;

        if (policy.ModelPool.Count > 0)
        {
            var affordable = policy.ModelPool
                .Select(name => state.Models.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Where(m => m is not null
                    && CanAffordModel(state, m)
                    && (!m.IsPremium || policy.AllowPremium)
                    && IsBudgetComfortable(state, m, budgetRatio))
                .ToList();
            if (affordable.Count > 0)
            {
                if (excludeFamily is null)
                {
                    return affordable[PoolRng.Next(affordable.Count)]!;
                }

                var crossFamily = affordable
                    .Where(m => !string.Equals(m!.EffectiveFamily, excludeFamily, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (crossFamily.Count > 0)
                {
                    return crossFamily[PoolRng.Next(crossFamily.Count)]!;
                }
            }
        }

        var primary = state.Models.FirstOrDefault(model => string.Equals(model.Name, policy.PrimaryModel, StringComparison.OrdinalIgnoreCase))
            ?? defaultModel;
        var fallback = state.Models.FirstOrDefault(model => string.Equals(model.Name, policy.FallbackModel, StringComparison.OrdinalIgnoreCase))
            ?? defaultModel;

        // Prefer cross-family candidates when excludeFamily is set
        if (excludeFamily is not null)
        {
            var crossCandidates = new[] { primary, fallback }
                .Where(m => m.Cost > 0
                    && !string.Equals(m.EffectiveFamily, excludeFamily, StringComparison.OrdinalIgnoreCase)
                    && CanAffordModel(state, m)
                    && (!m.IsPremium || policy.AllowPremium)
                    && IsBudgetComfortable(state, m, budgetRatio))
                .ToList();
            if (crossCandidates.Count > 0)
            {
                return crossCandidates[0];
            }
        }

        if (CanAffordModel(state, primary)
            && (!primary.IsPremium || policy.AllowPremium)
            && IsBudgetComfortable(state, primary, budgetRatio))
        {
            return primary;
        }

        if (fallback.Cost > 0
            && CanAffordModel(state, fallback)
            && IsBudgetComfortable(state, fallback, budgetRatio))
        {
            return fallback;
        }

        var light = state.Models
            .Where(model => model.Cost > 0 && model.Cost < (fallback.Cost > 0 ? fallback.Cost : double.MaxValue))
            .OrderBy(model => model.Cost)
            .FirstOrDefault();
        if (light is not null && CanAffordModel(state, light))
        {
            return light;
        }

        return defaultModel;
    }

    public void CommitCredits(WorkspaceState state, ModelDefinition model)
    {
        state.Budget.CreditsCommitted += model.Cost;
        if (model.IsPremium)
        {
            state.Budget.PremiumCreditsCommitted += model.Cost;
        }
    }

    public bool CanAffordModel(WorkspaceState state, ModelDefinition model)
    {
        // Check project-wide budget cap
        var totalAfter = state.Budget.CreditsCommitted + model.Cost;
        if (totalAfter > state.Budget.TotalCreditCap)
        {
            return false;
        }

        if (model.IsPremium)
        {
            var premiumAfter = state.Budget.PremiumCreditsCommitted + model.Cost;
            if (premiumAfter > state.Budget.PremiumCreditCap)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsBudgetComfortable(WorkspaceState state, ModelDefinition model, double budgetRatio)
    {
        if (model.Cost == 0) return true;
        
        // Check if model cost exceeds per-run limits even if project budget is low
        // This allows expensive models for critical single-run tasks
        if (model.IsPremium && model.Cost <= state.Budget.PerRunPremiumLimit)
        {
            return true; // Affordable within per-run premium limit
        }
        if (!model.IsPremium && model.Cost <= state.Budget.PerRunCreditLimit)
        {
            return true; // Affordable within per-run standard limit
        }
        
        // Enforce project-level budget pressure thresholds
        if (model.IsPremium) return budgetRatio > 0.30;
        if (model.Cost >= 1) return budgetRatio > 0.15;
        return true;
    }
}
