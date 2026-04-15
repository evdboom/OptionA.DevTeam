namespace DevTeam.Core;

public sealed class BudgetService : IBudgetService
{
    private static readonly Random PoolRng = new();

    public ModelDefinition SelectModelForRole(WorkspaceState state, string roleSlug)
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
                    && IsBudgetComfortable(m, budgetRatio))
                .ToList();
            if (affordable.Count > 0)
            {
                return affordable[PoolRng.Next(affordable.Count)]!;
            }
        }

        var primary = state.Models.FirstOrDefault(model => string.Equals(model.Name, policy.PrimaryModel, StringComparison.OrdinalIgnoreCase))
            ?? defaultModel;
        var fallback = state.Models.FirstOrDefault(model => string.Equals(model.Name, policy.FallbackModel, StringComparison.OrdinalIgnoreCase))
            ?? defaultModel;

        if (CanAffordModel(state, primary)
            && (!primary.IsPremium || policy.AllowPremium)
            && IsBudgetComfortable(primary, budgetRatio))
        {
            return primary;
        }

        if (fallback.Cost > 0
            && CanAffordModel(state, fallback)
            && IsBudgetComfortable(fallback, budgetRatio))
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

    private static bool IsBudgetComfortable(ModelDefinition model, double budgetRatio)
    {
        if (model.Cost == 0) return true;
        if (model.IsPremium) return budgetRatio > 0.30;
        if (model.Cost >= 1) return budgetRatio > 0.15;
        return true;
    }
}
