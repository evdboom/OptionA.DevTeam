namespace DevTeam.UnitTests.Tests;

internal static class BudgetServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("SelectModel_ReturnsDefault_WhenNoPool", SelectModel_ReturnsDefault_WhenNoPool),
        new("SelectModel_ReturnsFromPool_WhenAffordable", SelectModel_ReturnsFromPool_WhenAffordable),
        new("SelectModel_ReturnsFallback_WhenPrimaryUnaffordable", SelectModel_ReturnsFallback_WhenPrimaryUnaffordable),
        new("CanAffordModel_ReturnsFalse_WhenOverBudget", CanAffordModel_ReturnsFalse_WhenOverBudget),
        new("CommitCredits_IncreasesBudgetUsed", CommitCredits_IncreasesBudgetUsed),
    ];

    // "user" role policy has no model pool, primary="gpt-5-mini", fallback="gpt-5-mini"
    // With no models in state.Models, defaultModel is built as { Name="gpt-5-mini", Cost=0 }
    private static Task SelectModel_ReturnsDefault_WhenNoPool()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 25, CreditsCommitted = 0 }
        };
        // No models → defaultModel fallback = { Name="gpt-5-mini", Cost=0 }

        var model = svc.SelectModelForRole(state, "user");

        Assert.That(model.Name == "gpt-5-mini", $"Expected gpt-5-mini but got '{model.Name}'");
        Assert.That(model.Cost == 0, $"Expected cost 0 but got {model.Cost}");
        return Task.CompletedTask;
    }

    // "developer" policy has pool = ["gpt-5.4", "claude-sonnet-4.6"]
    private static Task SelectModel_ReturnsFromPool_WhenAffordable()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = "gpt-5.4", Cost = 1 },
                new ModelDefinition { Name = "claude-sonnet-4.6", Cost = 1 }
            ],
            Budget = new BudgetState { TotalCreditCap = 100, CreditsCommitted = 0 }
        };

        var model = svc.SelectModelForRole(state, "developer");

        var poolNames = new[] { "gpt-5.4", "claude-sonnet-4.6" };
        Assert.That(poolNames.Contains(model.Name), $"Expected pool model but got '{model.Name}'");
        return Task.CompletedTask;
    }

    // "planner" policy has no pool. Primary = "claude-sonnet-4.6" (Cost=1).
    // Set cap so primary is unaffordable; fallback "claude-haiku-4.5" (Cost=0.1) is affordable.
    private static Task SelectModel_ReturnsFallback_WhenPrimaryUnaffordable()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = "claude-sonnet-4.6", Cost = 1 },
                new ModelDefinition { Name = "claude-haiku-4.5", Cost = 0.1 }
            ],
            Budget = new BudgetState { TotalCreditCap = 0.5, CreditsCommitted = 0 }
        };

        var model = svc.SelectModelForRole(state, "planner");

        Assert.That(model.Name == "claude-haiku-4.5", $"Expected claude-haiku-4.5 but got '{model.Name}'");
        return Task.CompletedTask;
    }

    private static Task CanAffordModel_ReturnsFalse_WhenOverBudget()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 5, CreditsCommitted = 5 }
        };
        var model = new ModelDefinition { Name = "any-model", Cost = 1 };

        var result = svc.CanAffordModel(state, model);

        Assert.That(!result, "Expected CanAffordModel to return false when over budget");
        return Task.CompletedTask;
    }

    private static Task CommitCredits_IncreasesBudgetUsed()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 25, CreditsCommitted = 0 }
        };
        var model = new ModelDefinition { Name = "gpt-5.4", Cost = 2 };

        svc.CommitCredits(state, model);

        Assert.That(state.Budget.CreditsCommitted == 2,
            $"Expected CreditsCommitted=2 but got {state.Budget.CreditsCommitted}");
        return Task.CompletedTask;
    }
}
