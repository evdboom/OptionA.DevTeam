namespace DevTeam.UnitTests.Tests;

internal static class BudgetServiceTests
{
    public static IEnumerable<TestCase> GetTests() =>
    [
        new("SelectModel_ReturnsDefault_WhenNoPool", SelectModel_ReturnsDefault_WhenNoPool),
        new("SelectModel_ReturnsFromPool_WhenAffordable", SelectModel_ReturnsFromPool_WhenAffordable),
        new("SelectModel_ReturnsFallback_WhenPrimaryUnaffordable", SelectModel_ReturnsFallback_WhenPrimaryUnaffordable),
        new("CanAffordModel_ReturnsFalse_WhenOverBudget", CanAffordModel_ReturnsFalse_WhenOverBudget),
        new("CanAffordModel_ReturnsFalse_WhenBudgetExhausted", CanAffordModel_ReturnsFalse_WhenBudgetExhausted),
        new("CanAffordModel_ReturnsTrue_WhenExactlyEnoughBudget", CanAffordModel_ReturnsTrue_WhenExactlyEnoughBudget),
        new("CommitCredits_IncreasesBudgetUsed", CommitCredits_IncreasesBudgetUsed),
        new("CommitCredits_AccumulatesAcrossMultipleCalls", CommitCredits_AccumulatesAcrossMultipleCalls),
        new("InferFamily_DetectsOpenAI", InferFamily_DetectsOpenAI),
        new("InferFamily_DetectsAnthropic", InferFamily_DetectsAnthropic),
        new("InferFamily_DetectsGoogle", InferFamily_DetectsGoogle),
        new("InferFamily_FallsBackToOther", InferFamily_FallsBackToOther),
        new("SelectModel_PrefersOtherFamily_WhenExcludeFamilySet", SelectModel_PrefersOtherFamily_WhenExcludeFamilySet),
        new("SelectModel_FallsBackToSameFamily_WhenNoCrossOption", SelectModel_FallsBackToSameFamily_WhenNoCrossOption),
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

    private static Task CanAffordModel_ReturnsFalse_WhenBudgetExhausted()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 5, CreditsCommitted = 5 }
        };
        // Budget exactly at cap; any positive-cost model must be rejected
        var model = new ModelDefinition { Name = "any-model", Cost = 1 };

        var result = svc.CanAffordModel(state, model);

        Assert.That(!result, "Expected CanAffordModel to return false when budget is exactly at cap and model has positive cost");
        return Task.CompletedTask;
    }

    private static Task CanAffordModel_ReturnsTrue_WhenExactlyEnoughBudget()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 10, CreditsCommitted = 8 }
        };
        // remaining = 2, cost = 2 → totalAfter = 10 == cap → should be affordable (not strictly over)
        var model = new ModelDefinition { Name = "exact-model", Cost = 2 };

        var result = svc.CanAffordModel(state, model);

        Assert.That(result, "Expected CanAffordModel to return true when committed + cost == cap exactly");
        return Task.CompletedTask;
    }

    private static Task CommitCredits_AccumulatesAcrossMultipleCalls()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Budget = new BudgetState { TotalCreditCap = 25, CreditsCommitted = 0 }
        };
        var model = new ModelDefinition { Name = "gpt-5.4", Cost = 1 };

        svc.CommitCredits(state, model);
        svc.CommitCredits(state, model);
        svc.CommitCredits(state, model);

        Assert.That(state.Budget.CreditsCommitted == 3,
            $"Expected CreditsCommitted=3 after 3 commits but got {state.Budget.CreditsCommitted}");
        return Task.CompletedTask;
    }

    private static Task InferFamily_DetectsOpenAI()
    {
        Assert.That(ModelDefinition.InferFamily("gpt-5.4") == "openai", "gpt-5.4 should be openai");
        Assert.That(ModelDefinition.InferFamily("o1-preview") == "openai", "o1-preview should be openai");
        Assert.That(ModelDefinition.InferFamily("o3-mini") == "openai", "o3-mini should be openai");
        return Task.CompletedTask;
    }

    private static Task InferFamily_DetectsAnthropic()
    {
        Assert.That(ModelDefinition.InferFamily("claude-sonnet-4.6") == "anthropic", "claude-sonnet-4.6 should be anthropic");
        Assert.That(ModelDefinition.InferFamily("claude-opus-4.5") == "anthropic", "claude-opus-4.5 should be anthropic");
        return Task.CompletedTask;
    }

    private static Task InferFamily_DetectsGoogle()
    {
        Assert.That(ModelDefinition.InferFamily("gemini-3.1-pro-preview") == "google", "gemini-3.1-pro-preview should be google");
        return Task.CompletedTask;
    }

    private static Task InferFamily_FallsBackToOther()
    {
        Assert.That(ModelDefinition.InferFamily("llama-3-70b") == "other", "llama-3-70b should be other");
        Assert.That(ModelDefinition.InferFamily("") == "other", "empty should be other");
        return Task.CompletedTask;
    }

    // reviewer policy: primary = claude-opus-4.6 (anthropic). When excludeFamily=anthropic,
    // gpt-5.4 (openai) should be selected from the explicit model list.
    private static Task SelectModel_PrefersOtherFamily_WhenExcludeFamilySet()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = "claude-opus-4.6", Cost = 1, IsPremium = true },
                new ModelDefinition { Name = "gpt-5.4", Cost = 1, IsPremium = false }
            ],
            Budget = new BudgetState { TotalCreditCap = 100, CreditsCommitted = 0, PremiumCreditCap = 100 }
        };

        var model = svc.SelectModelForRole(state, "reviewer", excludeFamily: "anthropic");

        Assert.That(model.Name == "gpt-5.4", $"Expected gpt-5.4 (non-anthropic) but got '{model.Name}'");
        return Task.CompletedTask;
    }

    // When all models are the same family as excludeFamily, fall back gracefully.
    private static Task SelectModel_FallsBackToSameFamily_WhenNoCrossOption()
    {
        var svc = new BudgetService();
        var state = new WorkspaceState
        {
            Models =
            [
                new ModelDefinition { Name = "claude-opus-4.6", Cost = 1, IsPremium = true },
                new ModelDefinition { Name = "claude-haiku-4.5", Cost = 0.1, IsPremium = false }
            ],
            Budget = new BudgetState { TotalCreditCap = 100, CreditsCommitted = 0, PremiumCreditCap = 100 }
        };

        // All models are anthropic; excludeFamily = anthropic → should still return a model (not null)
        var model = svc.SelectModelForRole(state, "reviewer", excludeFamily: "anthropic");

        Assert.That(model is not null, "Expected a model even when no cross-family option exists");
        Assert.That(!string.IsNullOrEmpty(model!.Name), "Expected a non-empty model name when falling back");
        return Task.CompletedTask;
    }
}
